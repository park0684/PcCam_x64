using System;

namespace PcCam_x64.Views
{
    /// <summary>
    /// 시스템 트레이 View 인터페이스.
    /// 
    /// 트레이 메뉴는 사용자가 송출을 임의로 제어하지 못하도록
    /// 인증상태, 설정, 종료만 제공한다.
    /// </summary>
    public interface ITrayView
    {
        /// <summary>
        /// 설정 화면 열기 요청.
        /// </summary>
        event EventHandler SettingRequested;

        /// <summary>
        /// 프로그램 종료 요청.
        /// </summary>
        event EventHandler ExitRequested;

        /// <summary>
        /// 인증 상태 표시 문구를 변경한다.
        /// </summary>
        void SetAuthStatusText(string text);

        /// <summary>
        /// 트레이 툴팁/상태 텍스트를 변경한다.
        /// </summary>
        void SetStatusText(string text);

        /// <summary>
        /// 정보 메시지를 표시한다.
        /// </summary>
        void ShowInfo(string message);

        /// <summary>
        /// 오류 메시지를 표시한다.
        /// </summary>
        void ShowError(string message);

        /// <summary>
        /// 확인용 비밀번호 입력창을 표시한다.
        /// </summary>
        string PromptPassword(string title, string message);

        /// <summary>
        /// 프로그램을 종료한다.
        /// </summary>
        void CloseView();
    }
}