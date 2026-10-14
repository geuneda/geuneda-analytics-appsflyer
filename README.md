# Geuneda Analytics AppsFlyer

`com.geuneda.analytics` 의 AppsFlyer sink 입니다(AppsFlyer Unity 플러그인 7.x). 이벤트를 `logEvent` 로 보내고, 표준 이벤트(구매, 광고 노출 수익)를 AppsFlyer 표준으로 바꿉니다.

| 타입 | 설명 |
|------|------|
| `AppsFlyerAnalyticsSink` | sink(`AnalyticsSinks.AppsFlyer`). `MarkReady()` 로 준비를 알림 |
| `AppsFlyerAnalyticsSinkOptions` | `HandleEvent`(게임이 직접 보낼 이벤트) |

네임스페이스는 `Geuneda.Analytics.AppsFlyer` 입니다. 최소 Unity 는 2023.1 입니다(플러그인 7.x 의 `Awaitable`).

## 설치

AppsFlyer Unity 플러그인 7.x(`appsflyer-unity-plugin`, 어셈블리 `AppsFlyer`)를 먼저 설치합니다(AppsFlyer 문서의 UPM git URL 또는 .unitypackage). 그다음 의존 패키지와 함께 추가합니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.analytics": "https://github.com/geuneda/geuneda-analytics.git",
    "com.geuneda.analytics.appsflyer": "https://github.com/geuneda/geuneda-analytics-appsflyer.git"
  }
}
```

SDK 없이 설치하면 컴파일 오류가 납니다. define `GEUNEDA_SKIP_VENDOR_ADAPTERS` 가 있으면 컴파일되지 않습니다(호스트용).

## 사용 예

```csharp
var appsFlyer = new AppsFlyerAnalyticsSink();
analytics.AddSink(appsFlyer);

await AppsFlyer.init(devKey, iosAppId);
await AppsFlyer.start();
appsFlyer.MarkReady();   // 기다리던 이벤트와 사용자 ID 를 보냄
```

## 동작

- **준비**: AppsFlyer 초기화(`init`, `start`)는 게임이 합니다. `MarkReady` 전 이벤트는 분석 서비스 큐에서 기다리고, 사용자 ID 는 sink 가 들고 있다가 넘깁니다. `MarkReady` 는 사용자 ID 를 넘긴 뒤에 준비 상태가 되고, 넘기기가 실패하면 Error 로그를 남기고 준비되지 않으니 다시 부릅니다.
- **사용자 귀속**: 이벤트마다 기록할 때의 사용자 ID 로 `setCustomerUserId` 를 맞춘 뒤 보내고, 배치가 끝나면 지금 ID 로 되돌립니다.
- **일반 이벤트**: `logEvent(이름, 값)`. 값은 문자열(정수, 실수 `R` 형식, 불리언 `true`/`false`, 문화권 무관)입니다.
- **구매**: `af_purchase`(`af_revenue` = 단가 × 수량, `af_price`, `af_currency`, `af_content_id`, `af_quantity`, `af_order_id`) + 추가 파라미터.
- **광고 노출 수익**: `logAdRevenue`. 수익 네트워크는 `ad_source`(없으면 플랫폼), 미디에이션은 플랫폼 이름으로 고릅니다(AppLovin/MAX → `ApplovinMax`, LevelPlay/ironSource → `IronSource`, AdMob → `GoogleAdMob`, Unity → `Unity` 등, 모르는 이름은 `Custom`). 추가 정보는 `ad_unit`, `ad_type`, `placement` 와 추가 파라미터(예: `country`)입니다.
- **사용자**: `setCustomerUserId`. 7.x RPC 계약은 null·빈 문자열을 받지 않아(Android 는 1자 이상) 고객 사용자 ID 를 지울 수 없습니다. 그래서 null(로그아웃, 로그인 전 이벤트)은 보내지 않고 마지막 ID 가 남습니다. 로그아웃 뒤 이벤트를 다른 사용자와 나눠야 하면 `HandleEvent` 로 직접 처리합니다. AppsFlyer 에는 사용자 속성이 없어 `SetUserProperty` 는 아무것도 하지 않습니다.
- **게임 처리**: `HandleEvent` 가 true 를 돌려준 이벤트는 보내지 않습니다.
- `setCustomerUserId` 처럼 결과를 기다리지 않는 호출의 예외는 Error 로그로 남깁니다.
