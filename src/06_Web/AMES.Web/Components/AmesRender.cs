using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AMES.Web.Components;

/// <summary>
/// 화면(@page) 공용 렌더 모드(11-19 화면 로딩 개선).
/// 기본 InteractiveServer 는 서버가 먼저 HTML 을 미리 그리고(prerender) 실시간 연결 뒤 같은 화면을 다시 초기화해
/// 화면마다 OnInitialized 의 DB 조회가 두 번 돌고, 그동안 화면은 보이지만 눌리지 않았다. 미리 그리기를 끄면 한 번만 돈다.
/// 연결 전 잠깐 비는 본문은 MainLayout 의 로딩 표시(.ames-page-host:empty)가 채운다.
/// 미리 그린 결과를 넘겨받는(PersistentComponentState) 화면과 레이아웃 부품(메뉴·TopBar)은 그대로 InteractiveServer 를 쓴다.
/// </summary>
public static class AmesRender
{
    public static readonly IComponentRenderMode Server = new InteractiveServerRenderMode(prerender: false);
}
