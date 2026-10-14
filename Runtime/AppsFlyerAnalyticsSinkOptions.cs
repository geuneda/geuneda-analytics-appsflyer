using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.AppsFlyer
{
	/// <summary><see cref="AppsFlyerAnalyticsSink"/> 설정입니다.</summary>
	public sealed class AppsFlyerAnalyticsSinkOptions
	{
		/// <summary>
		/// 게임이 이벤트를 직접 보낼 때 씁니다. true 를 돌려주면 이 sink 는 그 이벤트를 보내지 않습니다. 메인 스레드에서 이벤트마다 불립니다.
		/// </summary>
		public Func<AnalyticsEvent, bool> HandleEvent { get; set; }
	}
}
