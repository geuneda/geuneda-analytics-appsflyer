using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AppsFlyerSDK;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.AppsFlyer
{
	/// <summary>
	/// AppsFlyer 로 이벤트를 보내는 sink 입니다(AppsFlyer Unity 플러그인 7.x). 파라미터 값은 문자열(문화권 무관)로 보냅니다.
	/// 표준 이벤트는 AppsFlyer 표준으로 바꿉니다: 구매는 <c>af_purchase</c>(<c>af_revenue</c>, <c>af_currency</c>, <c>af_content_id</c>, <c>af_quantity</c>, <c>af_order_id</c>),
	/// 광고 노출 수익은 <c>logAdRevenue</c>(플랫폼 이름으로 미디에이션을 고르고, 모르는 이름은 <c>Custom</c>).
	/// </summary>
	/// <remarks>
	/// AppsFlyer 초기화(<c>AppsFlyer.init</c>, <c>AppsFlyer.start</c>)는 게임이 하고 끝나면 <see cref="MarkReady"/> 를 부릅니다. 그 전에 기록한 이벤트는 분석 서비스가 큐에 두었다가 보내고,
	/// 사용자 ID 는 이 sink 가 들고 있다가 준비되면 넘깁니다. 이벤트마다 기록할 때의 사용자 ID 로 맞춘 뒤 보내고, 배치가 끝나면 지금 ID 로 되돌립니다.
	/// AppsFlyer 7.x 는 고객 사용자 ID 를 지울 수 없어(RPC 계약이 null·빈 문자열을 받지 않음) null 은 보내지 않습니다. 로그아웃한 뒤에도 마지막 ID 가 남습니다.
	/// AppsFlyer 에는 사용자 속성이 없어 <see cref="SetUserProperty"/> 는 아무것도 하지 않습니다.
	/// </remarks>
	public sealed class AppsFlyerAnalyticsSink : IAnalyticsSink
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics.AppsFlyer");

		private readonly IAppsFlyerApi _api;
		private readonly Func<AnalyticsEvent, bool> _handleEvent;

		// 분석 서비스가 알려 준 지금 사용자 ID 와 SDK 에 마지막으로 넘긴 ID(null 은 넘기지 않음)
		private string _userId;
		private string _sdkUserId;
		private bool _markingReady;

		public AppsFlyerAnalyticsSink(AppsFlyerAnalyticsSinkOptions options = null) : this(options, new AppsFlyerApi())
		{
		}

		internal AppsFlyerAnalyticsSink(AppsFlyerAnalyticsSinkOptions options, IAppsFlyerApi api)
		{
			_api = api;
			_handleEvent = options?.HandleEvent;
		}

		public AnalyticsSinks Id => AnalyticsSinks.AppsFlyer;

		public int MaxBatchSize => 50;

		public TimeSpan BatchDelay => TimeSpan.Zero;

		public bool IsReady { get; private set; }

		/// <summary>
		/// AppsFlyer 초기화(<c>init</c>, <c>start</c>)가 끝났음을 알립니다. 들고 있던 사용자 ID 를 넘긴 뒤에 준비 상태가 되고, 분석 서비스가 1초 안에 기다리던 이벤트를 보냅니다.
		/// 넘기기가 실패하면 Error 로그를 남기고 준비 상태가 되지 않으니 다시 부릅니다. 메인 스레드에서 부릅니다.
		/// </summary>
		public void MarkReady()
		{
			if (IsReady || _markingReady)
			{
				return;
			}

			_markingReady = true;
			MarkReadyAsync().Forget();
		}

		private async UniTaskVoid MarkReadyAsync()
		{
			try
			{
				await ApplyUserIdAsync(_userId);
				IsReady = true;
			}
			catch (Exception exception)
			{
				// 동기 API(MarkReady)에서 시작한 SDK 호출의 실패를 숨기지 않고 남깁니다. 준비 상태가 되지 않아 이벤트는 큐에서 기다립니다
				Logger.Error(exception, "AppsFlyer setCustomerUserId failed while marking ready; call MarkReady again");
			}
			finally
			{
				_markingReady = false;
			}
		}

		public async UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			var completed = false;

			try
			{
				foreach (var analyticsEvent in events)
				{
					// 서비스를 닫으면 남은 배치를 저장해 두도록 재시도로 돌려줍니다(이미 보낸 이벤트는 다음에 한 번 더 갈 수 있음)
					if (cancellationToken.IsCancellationRequested)
					{
						return AnalyticsSendResult.Retry;
					}

					// 게임이 직접 보내는 이벤트도 기록할 때의 사용자에게 붙습니다
					await ApplyUserIdAsync(analyticsEvent.UserId);

					if (_handleEvent != null && _handleEvent(analyticsEvent))
					{
						continue;
					}

					await Send(analyticsEvent);
				}

				completed = true;
			}
			finally
			{
				// 예외로 끝나도 지금 사용자로 되돌립니다. 되돌리기 실패는 원래 예외를 가리지 않게 로그로만 남깁니다
				if (completed)
				{
					await ApplyUserIdAsync(_userId);
				}
				else
				{
					Observe(ApplyUserIdAsync(_userId), "setCustomerUserId (restore)");
				}
			}

			return AnalyticsSendResult.Delivered;
		}

		public void SetUserId(string userId)
		{
			_userId = userId;

			if (IsReady)
			{
				Observe(ApplyUserIdAsync(userId), "setCustomerUserId");
			}
		}

		/// <summary>SDK 의 고객 사용자 ID 를 맞춥니다. null 은 지울 수 없어 넘기지 않습니다.</summary>
		private async UniTask ApplyUserIdAsync(string userId)
		{
			if (userId == null || userId == _sdkUserId)
			{
				return;
			}

			await _api.SetCustomerUserId(userId);
			_sdkUserId = userId;
		}

		public void SetUserProperty(string name, string value)
		{
			// AppsFlyer 에는 사용자 속성이 없습니다
		}

		private UniTask Send(AnalyticsEvent analyticsEvent)
		{
			if (AnalyticsPurchase.TryRead(analyticsEvent, out var purchase))
			{
				var values = Extras(analyticsEvent, AnalyticsPurchase.IsStandardParameter);
				values[AFInAppEvents.REVENUE] = Format(purchase.Value);
				values[AFInAppEvents.PRICE] = Format(purchase.Price);
				values[AFInAppEvents.CURRENCY] = purchase.Currency;
				values[AFInAppEvents.CONTENT_ID] = purchase.ProductId;
				values[AFInAppEvents.QUANTITY] = purchase.Quantity.ToString(CultureInfo.InvariantCulture);

				if (purchase.TransactionId != null)
				{
					values[AFInAppEvents.ORDER_ID] = purchase.TransactionId;
				}

				return _api.LogEvent(AFInAppEvents.PURCHASE, values);
			}

			if (AnalyticsAdImpression.TryRead(analyticsEvent, out var impression))
			{
				var additional = Extras(analyticsEvent, AnalyticsAdImpression.IsStandardParameter);
				AddOptional(additional, AdRevenueScheme.AD_UNIT, impression.UnitName);
				AddOptional(additional, AdRevenueScheme.AD_TYPE, impression.Format);
				AddOptional(additional, AdRevenueScheme.PLACEMENT, impression.Placement);
				return _api.LogAdRevenue(impression.Source ?? impression.Platform, Mediation(impression.Platform), impression.Currency, impression.Revenue,
					additional);
			}

			return _api.LogEvent(analyticsEvent.Name, Extras(analyticsEvent, _ => false));
		}

		/// <summary>플랫폼 이름(대소문자·공백·밑줄·하이픈 무시)으로 AppsFlyer 미디에이션을 고릅니다. 모르는 이름은 <see cref="MediationNetwork.Custom"/> 입니다.</summary>
		internal static MediationNetwork Mediation(string platform)
		{
			var normalized = platform.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

			switch (normalized)
			{
				case "applovin":
				case "applovinmax":
				case "max":
					return MediationNetwork.ApplovinMax;
				case "ironsource":
				case "levelplay":
				case "unitylevelplay":
					return MediationNetwork.IronSource;
				case "admob":
				case "googleadmob":
					return MediationNetwork.GoogleAdMob;
				case "unity":
				case "unityads":
					return MediationNetwork.Unity;
				case "appodeal":
					return MediationNetwork.Appodeal;
				case "fyber":
					return MediationNetwork.Fyber;
				case "admost":
					return MediationNetwork.Admost;
				case "topon":
					return MediationNetwork.Topon;
				case "tradplus":
					return MediationNetwork.Tradplus;
				case "yandex":
					return MediationNetwork.Yandex;
				case "chartboost":
					return MediationNetwork.ChartBoost;
				default:
					return MediationNetwork.Custom;
			}
		}

		private static Dictionary<string, string> Extras(AnalyticsEvent analyticsEvent, Func<string, bool> isStandard)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var parameter in analyticsEvent.Parameters)
			{
				if (!isStandard(parameter.Name))
				{
					values[parameter.Name] = parameter.Value.ToString();
				}
			}

			return values;
		}

		private static void AddOptional(Dictionary<string, string> values, string key, string value)
		{
			if (value != null)
			{
				values[key] = value;
			}
		}

		private static string Format(double value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		private static void Observe(UniTask task, string operation)
		{
			ObserveAsync(task, operation).Forget();
		}

		private static async UniTaskVoid ObserveAsync(UniTask task, string operation)
		{
			// 동기 인터페이스(SetUserId)에서 부른 SDK 호출의 예외를 버리지 않고 로그로 남깁니다
			try
			{
				await task;
			}
			catch (Exception exception)
			{
				Logger.Error(exception, $"AppsFlyer {operation} failed");
			}
		}
	}
}
