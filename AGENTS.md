# Geuneda.Analytics.AppsFlyer - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.analytics.appsflyer`
- **역할**: `com.geuneda.analytics` 의 AppsFlyer sink(결정 D3: 벤더 SDK 를 쓰는 게임만 설치하는 별도 패키지)
- **의존성**: `com.geuneda.analytics` 0.2.0(표준 이벤트), `com.geuneda.core` 0.4.0, `com.cysharp.unitask`. 벤더 SDK 는 UPM 의존성으로 적을 수 없어(에셋 설치·git URL) 게임이 먼저 설치합니다
- **SDK**: `AppsFlyerSDK.AppsFlyer`(7.x, 모든 호출이 `Awaitable`)와 전역 `AFInAppEvents`, `AdRevenueScheme`, `MediationNetwork`(어셈블리 `AppsFlyer`)
- **준비**: `MarkReady` 를 게임이 부름(`init`·`start` 뒤). 준비 전 `SetUserId` 는 sink 가 들고 있다가 넘기고, 넘기기(비동기)가 성공한 뒤에 준비 상태가 됨(실패하면 Error 로그 후 준비되지 않음, 진행 중 중복 호출 무시). 준비를 먼저 공개하면 큐의 이벤트가 사용자 ID 적용과 경쟁함(2026-10-15 리뷰 3회차)
- **출처**: 로드맵 4-1. 사용자 결정(2026-10-14): 표준 이벤트 계약 + 어댑터 변환, SDK 초기화는 게임이 하고 어댑터는 준비만 확인, 호스트에서만 제외 define

## 2. 구조

`AppsFlyerApi`/`IAppsFlyerApi`(SDK 호출, `Awaitable` 을 `UniTask` 로 기다림), `AppsFlyerAnalyticsSink`(미디에이션 고르기 `Mediation`), `AppsFlyerAnalyticsSinkOptions`. 테스트는 SDK 호출 인터페이스를 기록하는 대역으로 바꿔 변환 결과를 확인합니다(SDK 를 실제로 부르지 않음).

## 3. 규칙

- **호스트 제외**: 런타임·테스트 asmdef 모두 `defineConstraints: ["!GEUNEDA_SKIP_VENDOR_ADAPTERS"]` 입니다. geuneda-frameworks 호스트는 SDK 가 없어 ProjectSettings 에 이 define 을 두고, 게임은 define 없이 설치하면 컴파일됩니다. 다른 define 조건(예: SDK 감지용)을 더하지 마세요(게임마다 define 을 넣어야 하는 부담).
- **검증 환경**: 호스트에서는 컴파일되지 않으므로 SDK 를 넣은 임시 프로젝트에서 EditMode 테스트와 Android·iOS 플레이어 스크립트 컴파일(`PlayerBuildInterface.CompilePlayerScripts`)로 확인합니다(2026-10-15: SpellDefense 의 Firebase 13.12(네이티브 12.14)·Facebook·Airbridge 4.x, GitHub 의 AppsFlyer 7.0.3).
- **표준 이벤트**: `AnalyticsPurchase.TryRead`, `AnalyticsAdImpression.TryRead` 로 읽고 벤더 표준으로 바꿉니다. 표준이 아닌 파라미터(`IsStandardParameter` 가 false)는 추가 파라미터로 함께 보냅니다. 읽을 수 없는 표준 이름 이벤트는 일반 이벤트로 보냅니다.
- **게임 처리**: `HandleEvent` 가 true 면 보내지 않습니다. 이 함수의 예외는 분석 서비스가 sink 예외로 보고 Error 로그 후 그 배치를 버립니다.
- **사용자 귀속**: `SendAsync` 는 이벤트마다(`HandleEvent` 호출 전에도) 이벤트의 사용자 ID(`AnalyticsEvent.UserId`)로 `setCustomerUserId` 를 맞추고, 배치 끝(예외로 끝나도 `finally`)에 분석 서비스가 알려 준 지금 ID(`_userId`)로 되돌립니다. SDK 에 넘긴 ID(`_sdkUserId`)는 성공한 뒤에만 바꾸고 같으면 부르지 않습니다. null 은 지울 수 없어 넘기지 않습니다(아래). 이 순서를 빼면 기다렸거나 저장했던 이벤트가 나중 사용자에게 붙습니다(2026-10-15 리뷰).
- **취소**: 서비스를 닫으면 토큰이 취소됩니다. 취소된 토큰이면 보내지 않고 `Retry`(배치를 저장해 둠)를 돌려줍니다. AppsFlyer 는 이벤트 사이마다 확인하므로 배치 중간에 닫히면 이미 보낸 이벤트가 다음 실행에서 한 번 더 갈 수 있습니다.
- **sink 계약**: 모든 멤버는 메인 스레드에서 불립니다. `SendAsync` 는 SDK 를 동기로 부른 뒤 `Delivered` 입니다(SDK 가 자체 큐를 가짐). static 가변 상태는 로거뿐입니다(D2).
- 네임스페이스 `Geuneda.Analytics.AppsFlyer` 가 `AppsFlyer` 클래스 이름을 가리므로 SDK 호출은 `global::AppsFlyerSDK.AppsFlyer` 로 씁니다.
- 플러그인 7.x 는 Unity 2023.1 이상(`Awaitable`)이라 이 패키지의 최소 Unity 도 2023.1 입니다. 6.x(`sendEvent`) API 는 지원하지 않습니다.
- 동기 인터페이스(`SetUserId`)에서 부른 SDK 호출은 `ObserveAsync` 로 예외를 Error 로그로 남깁니다(결과로 숨기지 않음).
- 7.x RPC 스키마(`appsflyer-plugins-rpc-schema.json` 의 `setCustomerUserId`)는 `customerId` 를 null 이 아닌 문자열(Android 1자 이상)로 정합니다. null 을 보내지 않고 마지막 ID 를 남깁니다(지울 방법이 없음, README 에 적음).

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Analytics.AppsFlyer.Tests.EditMode`): 일반 이벤트 변환, 표준 이벤트 변환, `HandleEvent`, 사용자 정보, 이름·개수·길이 제한(해당하는 것).

## 5. 문서

공개 API 를 바꾸면 `README.md`, `CHANGELOG.md`, 호스트 `claude-skills/geuneda-analytics/` 스킬을 함께 고칩니다.
