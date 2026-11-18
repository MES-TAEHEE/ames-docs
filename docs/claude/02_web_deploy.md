# AMES.Web 배포(IIS·접속문자열·CORS·watchdog·스마트 앱 컨트롤)

> `CLAUDE.md` 에서 분리한 상세 문서입니다(원문 그대로, 11-18 토큰 절감 정리). 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽으세요.

## AMES.Web 배포

### Web/API 접속문자열·CORS

- Web/API의 `appsettings*.json`에는 DB 비밀번호를 넣지 않는다. 실행 프로세스 또는 각 IIS 앱풀에 `ConnectionStrings__AMES`로 전체 접속문자열을 설정한다. 비어 있으면 DB 접근 전에 설정 오류로 중단한다. POP·InjAgent 설정은 별개다.
- 서버 배포 전 Web 앱풀과 API 앱풀 **각각** 환경변수를 설정해야 한다. 기존 `appsettings.Production.json`에 남은 접속문자열도 제거한다. 앱풀 환경변수는 IIS 설정에 보관되어 재게시해도 유지된다. 이 변경만으로 서버 설정이나 비밀번호가 자동 변경되지는 않는다.
- 개발 시 현재 PowerShell 프로세스의 `ConnectionStrings__AMES`를 설정하고 실행한다. `launchSettings.json`에는 비밀번호를 넣지 않는다. `publish-*.ps1 -Target Local -DbTarget Local`도 이 환경변수 또는 명시적인 `-LocalDbConn`을 사용하며 접속문자열을 출력하지 않는다. 비밀번호가 든 명령을 셸 기록에 남기지 않는다.
- API CORS는 기본 빈 허용 목록이다. 네이티브 PDA·Tablet 및 서버 간 호출에는 CORS 설정이 필요 없다. 브라우저에서 API를 다른 출처로 직접 호출할 때만 API 앱풀에 `Cors__AllowedOrigins__0=https://실제-웹-호스트`처럼 **scheme + host + port**를 등록한다. 경로·마지막 `/`·와일드카드는 넣지 않는다. 추가 출처는 `__1`, `__2`로 지정한다. 같은 출처 Web의 Blazor Server→API 호출에는 추가하지 않는다.
- `tools/db-route.ps1`의 구형 자동 전환은 접속문자열이 설정 파일에 있는 경우만 지원한다. 환경변수 전용 구성은 값을 유지하며 자동으로 삭제·전환하지 않는다.

로컬 IIS(`w3wp`)로 구동된다. `dotnet run`/IIS Express 아님.

```powershell
tools\publish-web.ps1                               # 개발서버 복사용 패키지 → publish\AMES.Web
tools\publish-web.ps1 -Zip                          # + zip
tools\publish-web.ps1 -Target Local                 # 로컬 IIS 반영 (개발서버 DB)
tools\publish-web.ps1 -Target Local -DbTarget Local # 로컬 IIS 반영 (비상: 로컬 DB)
tools\publish-api.ps1 [-Zip] [-Target Local [-EnsureIis] [-DbTarget Local]]   # AMES.Api 도 같은 방식 → publish\AMES.Api / C:\inetpub\wwwroot\Source\AMES.Api
```

AMES.Api 는 로컬 IIS 사이트 `AMES.Api`(앱풀 `AMES.Api`, `http *:5210` — Web 의 `Services:ApiBaseUrl` 과 같은 포트, 물리 경로 `C:\inetpub\wwwroot\Source\AMES.Api`)로 구동된다(09-16). 앱풀은 Web 과 반드시 분리한다 — in-process 는 앱풀 하나에 앱 하나만 허용해 같이 두면 500.30 이 난다. 사이트·앱풀이 없는 PC 에서는 `-EnsureIis`(관리자)로 만든다. IIS 아래에서는 appsettings 의 `Urls` 가 무시되고 바인딩 포트를 쓴다. 기동 확인은 `GET http://localhost:5210/api/health`. **Swagger(`/swagger`)는 Development 이거나 설정 `Swagger:Enabled=true` 일 때만 켜진다**(10-14 안영훈 보안 강화로 Development 전용이 됐고, 10-15 개발서버용으로 설정을 더함) — 11-13 부터 프로젝트 `src/04_Api/AMES.Api/web.config` 에 `Swagger__Enabled=true` 가 들어 있어 Jenkins 개발서버 배포도 켜진다(그 전에는 Jenkins 가 web.config 를 덮어써 손으로 넣은 값이 사라졌다). `-Target Local` 로컬 IIS 반영과 운영 서버용 패키지(`publish-api.ps1 -DevServerSwagger:$false`)는 이 값을 뺀다. 개발서버 문서 주소: `https://desktop-poedibs.tail526f68.ts.net/api/scalar/`(Tailscale 경로 `/api` 아래, 엔드포인트는 `…/api/api/...`).

- **라이브 IIS 폴더로 직접 게시하면 반드시 실패한다** — `w3wp`가 `AMES.Web.dll`을 잡고 있다.
  `-Target Local`은 `app_offline.htm`을 먼저 떨궈 ANCM이 앱을 내리게 하므로 잠금이 풀리고 관리자 권한도 필요 없다.
- `**/Properties/PublishProfiles/`는 gitignore 대상 → 게시 프로필 수정은 그 PC에만 적용된다.
- `dotnet publish` CLI는 pubxml의 `PublishUrl`을 무시한다(VS 전용). `-o`로 지정할 것.

**앱풀 필수 설정** (`loadUserProfile` 뿐 아니라 **`setProfileEnvironment` 도** 켜야 한다. 후자는 IIS 관리자 UI에 없다):

```powershell
appcmd set apppool "AMES.Web" /processModel.loadUserProfile:true /processModel.setProfileEnvironment:true
```

끄면 Data Protection이 ephemeral 키를 써서 **앱풀 재활용마다 로그인 사용자가 전원 로그아웃**된다.
`dist/setup-iis.ps1` 이 이 두 설정과 Data Protection 키 폴더(앱풀 계정 Modify)를 같이 구성한다(09-19). 이미 운영 중인 서버는 `dist\setup-iis.ps1 -ConfigOnly [-PoolName …]`(관리자) — 게시·사이트 경로·바인딩·런타임/계정 설정·`iisreset` 은 건드리지 않고 두 설정 + 키 폴더 권한만 적용한 뒤 그 앱풀만 재활용한다. 옵션 없이 돌리면 `C:\inetpub\ames-web`·포트 5000 으로 **게시하고 사이트 경로·바인딩을 바꾸므로** 기존 서버에서는 반드시 `-ConfigOnly` 를 쓴다.

**09-18 부터 AMES.Web 은 이 설정에 기대지 않는다** — `Program.cs` 가 키를 `%ProgramData%\AMES\DataProtection-Keys\AMES.Web`(설정 `DataProtection:KeyPath` 로 변경 가능)에 두고 머신 범위 DPAPI 로 암호화한다. 배포 폴더 안에 두지 않는 이유는 `publish-web.ps1` 의 `robocopy /MIR` 가 게시 때마다 지우기 때문이다. 폴더를 못 만들거나 쓰기 권한이 없으면 기동은 계속하고 경고 로그(`Data Protection key folder … is not writable`)만 남긴 채 프레임워크 기본 동작으로 돌아가므로, 그 경우에만 위 앱풀 설정이 다시 필요하다. 키는 서버마다 따로 생기며, 이 버전을 처음 올릴 때는 키가 바뀌어 **로그인 사용자가 한 번 전원 로그아웃**된다.

**서버 반영 절차**: ① 앱풀 중지 또는 `app_offline.htm` 배치 → ② `publish\AMES.Web\*` 덮어쓰기 → ③ `app_offline.htm` 제거 / 앱풀 시작.
서버 사전 조건은 **ASP.NET Core Hosting Bundle**(IIS 연결 모듈 ANCM 용), 앱풀 "관리 코드 없음", 배포 폴더에 앱풀 계정 읽기/실행 권한.

**Web·Api 는 .NET 런타임을 앱 폴더에 넣어 게시한다**(10-14, `publish-web/api.ps1` 의 `--self-contained true` — 게시본 Web 약 170MB·Api 약 120MB). 전에는 PC 의 공용 런타임(`C:\Program Files\dotnet`)을 빌려 써서, Windows Update·VS 가 .NET 을 바꾸는 도중에 뜬 작업자 프로세스가 **500.31**(런타임 못 찾음)·**500.30**(기동 실패)으로 죽은 채 남았다(10-14 로컬·개발서버 실제 발생). in-process 호스팅은 기동에 실패한 프로세스가 살아 있는 동안 같은 오류만 돌려주고 `app_offline.htm` 으로도 내려가지 않으며, 앱풀 재활용으로만 복구된다. 런타임 버전은 게시한 PC 의 SDK 가 정하고(`*.runtimeconfig.json` 의 `includedFrameworks`), 보안 패치는 다음 게시 때 같이 들어간다.
**기동 실패 자동 복구 — `tools\iis-watchdog.ps1`**(서버마다 관리자로 `-Install` 1회, 예약 작업 「AMES IIS Watchdog」 SYSTEM·부팅 2분 뒤·5분마다, 로그 `%ProgramData%\AMES\iis-watchdog.log`): 앱풀(`AMES.Web`·`AMES.Api`)마다 그 사이트의 `http://localhost:{포트}/api/health` 를 불러 **ANCM 기동 실패 페이지(HTTP 500.3x)이거나 ANCM 종료 중 멈춤(HTTP 503.0, 오류 코드 `0x800704e7` — 10-15 로컬 Api 가 게시 뒤 이 상태로 1시간 남았다; 정상 재시작 중의 순간 503 과 가르려고 15초 뒤 다시 불러 그대로일 때만)일 때만** 그 앱풀을 재활용한다. 일반 500(앱 예외·DB 장애)·다른 503·`app_offline.htm`(배포 중)·응답 없음·멈춘 앱풀(배포 절차의 일부러 멈춤)은 로그만 남기고, 같은 앱풀은 10분 안에 다시 재활용하지 않는다. Windows PowerShell 5.1 의 `Invoke-WebRequest` 는 오류 응답 본문을 비워 넘기므로 본문 판정은 `HttpClient` 로 한다. 10-14 로컬 등록·검증(정상 무동작, 500.30 → 재활용 → 200, 쿨다운, 원인이 남으면 재시도 안내, app_offline·멈춘 앱풀 건너뜀, 작업 스케줄러 SYSTEM 실행으로 복구), **개발서버는 새 게시본 반영 때 그 PC 에서 `-Install` 필요**.

장애 원인은 **이벤트 뷰어 > 응용 프로그램 > `IIS AspNetCore Module V2`** 가 가장 확실하다. `web.config`의 stdout 로그는 `logs` 폴더 쓰기 권한이 없으면 조용히 실패한다. 원인 메시지가 안 보이면 서버의 배포 폴더(IIS 물리 경로 — `appcmd list vdir "AMES.Api/" /text:physicalPath`)에서 직접 띄워 콘솔 예외를 본다: `$env:ASPNETCORE_ENVIRONMENT="Production"; .\AMES.Api.exe --urls http://localhost:5298`(포트는 IIS 바인딩과 다르게 — 같으면 `Failed to bind` 로 먼저 죽는다).

**코드 무결성 정책(스마트 앱 컨트롤)이 AMES DLL 을 막음 — 감시 작업이 재활용해도 500.30 이 계속됨**(10-20 개발서버 Api 실제 발생): 위처럼 직접 띄우면 `System.IO.FileLoadException: Could not load file or assembly '…\AMES.Contracts.dll'. An Application Control policy has blocked this file. (0x800711C7)` 이 나고, 이벤트 뷰어 `Microsoft-Windows-CodeIntegrity/Operational` 에 w3wp.exe 가 그 DLL 을 막혔다는 3077·3033(정책 ID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` = Windows 11 **스마트 앱 컨트롤**)이 남는다. AMES DLL 은 서명이 없어 새로 빌드할 때마다 평판이 없는 파일이라 이 정책이 켜진 PC 에서는 막힌다(개발서버 `DESKTOP-POEDIBS` 는 Windows 11 PC — Windows Server 에는 스마트 앱 컨트롤이 없다). 확인(관리자 PowerShell): `Get-MpComputerStatus | Select-Object SmartAppControlState` · `Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy | Select-Object VerifiedAndReputablePolicyState`(0 꺼짐·1 켜짐·2 평가) · `CiTool.exe -lp -json` 의 적용 중(`IsEnforced`) 정책. **10-20 은 설정 화면에 「끄기」로 보였는데 실제 상태는 `On` 이었고, 스마트 앱 컨트롤을 켰다가 다시 끄자 정상이 됐다**(Windows 보안 → 앱 및 브라우저 컨트롤 → 스마트 앱 컨트롤 설정) — 끈 뒤 위 상태값이 꺼짐인지 확인하고 `AMES.Api`·`AMES.Web` 앱풀을 재활용한다(그래도 남으면 재부팅). zip 의 「인터넷에서 받은 파일」 표시를 지우는 `Unblock-File` 만으로는 풀리지 않는다. 같은 정책이 Web 의 AMES DLL 도 막을 수 있으므로 Api 만 고치고 끝내지 않는다. 적용 중인 정책이 이 ID 가 아니면 회사 WDAC 정책이라 IT 담당이 배포 폴더를 허용하거나 DLL 에 공인 코드 서명 인증서로 서명해야 한다(스마트 앱 컨트롤·WDAC 모두 자체 서명은 인정하지 않는다).

---
