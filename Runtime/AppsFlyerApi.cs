using System.Collections.Generic;
using AppsFlyerSDK;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.AppsFlyer
{
	/// <summary>AppsFlyer SDK 호출입니다. 테스트는 기록하는 구현으로 바꿉니다.</summary>
	internal interface IAppsFlyerApi
	{
		UniTask LogEvent(string name, Dictionary<string, string> values);

		UniTask LogAdRevenue(string monetizationNetwork, MediationNetwork mediationNetwork, string currency, double revenue,
			Dictionary<string, string> additionalParameters);

		/// <summary>고객 사용자 ID 를 정합니다. 7.x RPC 계약은 null·빈 문자열을 받지 않으므로 넘기지 않습니다.</summary>
		UniTask SetCustomerUserId(string userId);
	}

	/// <summary>
	/// 실제 AppsFlyer Unity 플러그인(7.x, <c>AppsFlyerSDK.AppsFlyer</c>)을 부릅니다. 이 어셈블리의 네임스페이스 <c>Geuneda.Analytics.AppsFlyer</c> 가 이름을 가리므로 완전한 이름으로 부릅니다.
	/// </summary>
	internal sealed class AppsFlyerApi : IAppsFlyerApi
	{
		public async UniTask LogEvent(string name, Dictionary<string, string> values)
		{
			await global::AppsFlyerSDK.AppsFlyer.logEvent(name, values);
		}

		public async UniTask LogAdRevenue(string monetizationNetwork, MediationNetwork mediationNetwork, string currency, double revenue,
			Dictionary<string, string> additionalParameters)
		{
			await global::AppsFlyerSDK.AppsFlyer.logAdRevenue(new AFAdRevenueData(monetizationNetwork, mediationNetwork, currency, revenue), additionalParameters);
		}

		public async UniTask SetCustomerUserId(string userId)
		{
			await global::AppsFlyerSDK.AppsFlyer.setCustomerUserId(userId);
		}
	}
}
