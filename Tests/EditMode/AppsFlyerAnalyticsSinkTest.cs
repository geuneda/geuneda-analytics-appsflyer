using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using AppsFlyerSDK;
using Cysharp.Threading.Tasks;
using Geuneda.Analytics.AppsFlyer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.AppsFlyer.Tests
{
	public sealed class AppsFlyerAnalyticsSinkTest
	{
		private static readonly AnalyticsAppInfo App = new AnalyticsAppInfo("i", "1.0", "android", "dev", "d", "o");

		private FakeApi _api;

		[SetUp]
		public void SetUp()
		{
			_api = new FakeApi();
		}

		private AppsFlyerAnalyticsSink Sink(Func<AnalyticsEvent, bool> handle = null)
		{
			var sink = new AppsFlyerAnalyticsSink(new AppsFlyerAnalyticsSinkOptions { HandleEvent = handle }, _api);
			sink.MarkReady();
			return sink;
		}

		private static AnalyticsEvent Event(string name, params (string Name, object Value)[] parameters)
		{
			return UserEvent(name, null, parameters);
		}

		private static AnalyticsEvent UserEvent(string name, string userId, params (string Name, object Value)[] parameters)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name,
				parameters.Select(p => new AnalyticsParameter(p.Name, AnalyticsValue.FromObject(p.Value))).ToArray(), DateTime.UtcNow, false, 1, null, userId,
				null, App);
		}

		private static void Send(AppsFlyerAnalyticsSink sink, params AnalyticsEvent[] events)
		{
			var task = sink.SendAsync(events, CancellationToken.None);
			Assert.IsTrue(task.Status.IsCompleted());
			Assert.AreEqual(AnalyticsSendResult.Delivered, task.GetAwaiter().GetResult());
		}

		[Test]
		public void GenericEvent_SendsInvariantStrings()
		{
			Send(Sink(), Event("stage_clear", ("stage", 3), ("ratio", 0.5), ("win", true), ("mode", "hard")));

			var (name, values, _) = _api.Events.Single();
			Assert.AreEqual("stage_clear", name);
			Assert.AreEqual("3", values["stage"]);
			Assert.AreEqual("0.5", values["ratio"]);
			Assert.AreEqual("true", values["win"]);
			Assert.AreEqual("hard", values["mode"]);
		}

		[Test]
		public void Purchase_BecomesAfPurchase()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.Purchase, ("product_id", "gem_100"), ("price", 1.5), ("quantity", 2), ("value", 3.0),
				("currency", "USD"), ("transaction_id", "tx"), ("player_level", 9)));

			var (name, values, _) = _api.Events.Single();
			Assert.AreEqual("af_purchase", name);
			Assert.AreEqual("3", values["af_revenue"]);
			Assert.AreEqual("1.5", values["af_price"]);
			Assert.AreEqual("USD", values["af_currency"]);
			Assert.AreEqual("gem_100", values["af_content_id"]);
			Assert.AreEqual("2", values["af_quantity"]);
			Assert.AreEqual("tx", values["af_order_id"]);
			Assert.AreEqual("9", values["player_level"]);
			Assert.IsFalse(values.ContainsKey("product_id"));
		}

		[Test]
		public void AdImpression_BecomesAdRevenue()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.AdImpression, ("ad_platform", "AppLovin"), ("ad_source", "AdMob"), ("ad_format", "rewarded"),
				("ad_unit_name", "u1"), ("ad_placement", "shop"), ("value", 0.002), ("currency", "USD"), ("country", "KR")));

			var revenue = _api.AdRevenues.Single();
			Assert.AreEqual("AdMob", revenue.Network);
			Assert.AreEqual(MediationNetwork.ApplovinMax, revenue.Mediation);
			Assert.AreEqual("USD", revenue.Currency);
			Assert.AreEqual(0.002, revenue.Revenue);
			Assert.AreEqual("u1", revenue.Additional[AdRevenueScheme.AD_UNIT]);
			Assert.AreEqual("rewarded", revenue.Additional[AdRevenueScheme.AD_TYPE]);
			Assert.AreEqual("shop", revenue.Additional[AdRevenueScheme.PLACEMENT]);
			Assert.AreEqual("KR", revenue.Additional["country"]);
		}

		[TestCase("AppLovin", MediationNetwork.ApplovinMax)]
		[TestCase("applovin_max", MediationNetwork.ApplovinMax)]
		[TestCase("LevelPlay", MediationNetwork.IronSource)]
		[TestCase("ironSource", MediationNetwork.IronSource)]
		[TestCase("AdMob", MediationNetwork.GoogleAdMob)]
		[TestCase("Something", MediationNetwork.Custom)]
		public void Mediation_IsChosenByPlatformName(string platform, MediationNetwork expected)
		{
			Assert.AreEqual(expected, AppsFlyerAnalyticsSink.Mediation(platform));
		}

		[Test]
		public void UserId_IsHeldUntilReady_AndFailuresAreLogged()
		{
			var sink = new AppsFlyerAnalyticsSink(null, _api);
			sink.SetUserId("u1");
			sink.SetUserProperty("tier", "gold");
			Assert.AreEqual(0, _api.UserIds.Count);

			sink.MarkReady();
			CollectionAssert.AreEqual(new[] { "u1" }, _api.UserIds);

			_api.FailUserId = true;
			LogAssert.Expect(LogType.Error, new Regex("AppsFlyer setCustomerUserId failed"));
			LogAssert.Expect(LogType.Exception, new Regex("native gone"));
			sink.SetUserId("u2");
		}

		[Test]
		public void Events_AreAttributedToTheUserAtLogTime_AndNullIsNeverSent()
		{
			var sink = Sink();
			sink.SetUserId("B");

			Send(sink, UserEvent("first", "A"), UserEvent("anonymous", null), UserEvent("second", "B"));
			sink.SetUserId(null);

			CollectionAssert.AreEqual(new[] { ("first", "A"), ("anonymous", "A"), ("second", "B") }, _api.Events.Select(e => (e.Name, e.UserId)));
			Assert.IsFalse(_api.UserIds.Contains(null), "AppsFlyer cannot clear the customer user id");
			Assert.AreEqual("B", _api.CurrentUserId);
		}

		[Test]
		public void MarkReady_BecomesReadyOnlyAfterTheUserIdIsApplied()
		{
			var sink = new AppsFlyerAnalyticsSink(null, _api);
			sink.SetUserId("u1");
			var pending = new UniTaskCompletionSource();
			_api.NextUserIdResult = pending.Task;

			sink.MarkReady();
			sink.MarkReady();
			Assert.IsFalse(sink.IsReady, "not ready while setCustomerUserId is in flight");

			pending.TrySetResult();
			Assert.IsTrue(sink.IsReady);
			CollectionAssert.AreEqual(new[] { "u1" }, _api.UserIds);
		}

		[Test]
		public void MarkReady_ThatFails_StaysNotReadyAndCanBeRetried()
		{
			var sink = new AppsFlyerAnalyticsSink(null, _api);
			sink.SetUserId("u1");
			_api.FailUserId = true;

			LogAssert.Expect(LogType.Error, new Regex("failed while marking ready"));
			LogAssert.Expect(LogType.Exception, new Regex("native gone"));
			sink.MarkReady();
			Assert.IsFalse(sink.IsReady);

			_api.FailUserId = false;
			sink.MarkReady();
			Assert.IsTrue(sink.IsReady);
		}

		[Test]
		public void CancelledToken_ReturnsRetryWithoutSending()
		{
			var cancelled = new CancellationToken(true);

			var task = Sink().SendAsync(new[] { Event("e") }, cancelled);

			Assert.AreEqual(AnalyticsSendResult.Retry, task.GetAwaiter().GetResult());
			Assert.AreEqual(0, _api.Events.Count);
		}

		[Test]
		public void FailedBatch_StillRestoresTheCurrentUser()
		{
			var sink = Sink(_ => throw new InvalidOperationException("game bug"));
			sink.SetUserId("B");

			var task = sink.SendAsync(new[] { UserEvent("first", "A") }, CancellationToken.None);

			Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
			Assert.AreEqual("B", _api.CurrentUserId);
		}

		[Test]
		public void HandleEvent_LetsTheGameSendItself()
		{
			Send(Sink(e => e.Name == "special"), Event("special"), Event("normal"));

			CollectionAssert.AreEqual(new[] { "normal" }, _api.Events.Select(e => e.Name));
		}

		private sealed class FakeApi : IAppsFlyerApi
		{
			public List<(string Name, Dictionary<string, string> Values, string UserId)> Events { get; } = new List<(string, Dictionary<string, string>, string)>();

			public List<(string Network, MediationNetwork Mediation, string Currency, double Revenue, Dictionary<string, string> Additional)> AdRevenues { get; } =
				new List<(string, MediationNetwork, string, double, Dictionary<string, string>)>();

			public List<string> UserIds { get; } = new List<string>();
			public bool FailUserId { get; set; }
			public string CurrentUserId { get; private set; }

			/// <summary>다음 SetCustomerUserId 가 돌려줄 작업. 끝나면 그 ID 를 기록합니다.</summary>
			public UniTask? NextUserIdResult { get; set; }

			public UniTask LogEvent(string name, Dictionary<string, string> values)
			{
				Events.Add((name, values, CurrentUserId));
				return UniTask.CompletedTask;
			}

			public UniTask LogAdRevenue(string monetizationNetwork, MediationNetwork mediationNetwork, string currency, double revenue,
				Dictionary<string, string> additionalParameters)
			{
				AdRevenues.Add((monetizationNetwork, mediationNetwork, currency, revenue, additionalParameters));
				return UniTask.CompletedTask;
			}

			public UniTask SetCustomerUserId(string userId)
			{
				if (FailUserId)
				{
					return UniTask.FromException(new InvalidOperationException("native gone"));
				}

				if (NextUserIdResult.HasValue)
				{
					var result = NextUserIdResult.Value;
					NextUserIdResult = null;
					return Complete(result, userId);
				}

				UserIds.Add(userId);
				CurrentUserId = userId;
				return UniTask.CompletedTask;
			}

			private async UniTask Complete(UniTask result, string userId)
			{
				await result;
				UserIds.Add(userId);
				CurrentUserId = userId;
			}
		}
	}
}
