using System;
using System.Collections.Generic;
using PcCam_x64.Models;

namespace PcCam_x64.Views
{
    /// <summary>
    /// PC CAM 설정 화면 인터페이스.
    /// 
    /// Presenter는 실제 WinForms 컨트롤을 직접 참조하지 않고,
    /// 이 인터페이스를 통해 설정값을 읽고 저장한다.
    /// </summary>
    public interface ISettingView
    {
        /// <summary>
        /// 적용 버튼 클릭 이벤트.
        /// 설정 저장 후 화면은 유지한다.
        /// </summary>
        event EventHandler ApplyRequested;

        /// <summary>
        /// 확인 버튼 클릭 이벤트.
        /// 설정 저장 후 화면을 닫는다.
        /// </summary>
        event EventHandler OkRequested;

        /// <summary>
        /// 취소 버튼 클릭 이벤트.
        /// 변경사항을 저장하지 않고 화면을 닫는다.
        /// </summary>
        event EventHandler CancelRequested;

        /// <summary>
        /// 인증 정보 제거 버튼 클릭 이벤트.
        /// 인증키가 없으면 등록, 인증키가 있으면 제거 역할을 수행한다.
        /// </summary>
        event EventHandler AuthActionRequested;


        /// <summary>
        /// 스트림 설정 목록.
        /// 0번: 주 모니터
        /// 1번: 보조 모니터
        /// </summary>
        List<StreamConfig> Streams { get; set; }

        /// <summary>
        /// ONVIF 사용자 ID.
        /// 1단계에서는 RTSP Read 인증에도 같이 사용한다.
        /// </summary>
        string OnvifUserId { get; set; }

        /// <summary>
        /// ONVIF 비밀번호.
        /// 1단계에서는 RTSP Read 인증에도 같이 사용한다.
        /// </summary>
        string OnvifPassword { get; set; }

        /// <summary>
        /// 인증키.
        /// 실제 인증 처리는 인증 DLL이 담당한다.
        /// </summary>
        string LicenseKey { get; set; }

        /// <summary>
        /// 장비명.
        /// 인증서버에 기록될 장비 식별명이다.
        /// </summary>
        string DeviceName { get; set; }

        /// <summary>
        /// 상세 로그 기록 여부.
        /// </summary>
        bool EnableDetailLog { get; set; }

        /// <summary>
        /// Windows 시작 시 자동 실행 여부.
        /// 1단계에서는 설정값만 저장하고 실제 등록은 후속 단계에서 구현한다.
        /// </summary>
        bool AutoStart { get; set; }

        /// <summary>
        /// 절전모드 해제 여부.
        /// </summary>
        bool PreventSleep { get; set; }

        /// <summary>
        /// 프로그램 시작 시 자동 송출 여부.
        /// 현재 트레이 메뉴에서 송출 시작/중지를 제공하지 않으므로 테스트 시 중요하다.
        /// </summary>
        bool AutoStartStreaming { get; set; }

        /// <summary>
        /// 설정 화면을 표시한다.
        /// </summary>
        void ShowForm();

        /// <summary>
        /// 설정 화면을 닫는다.
        /// </summary>
        void CloseForm();

        /// <summary>
        /// 정보 메시지 표시.
        /// </summary>
        void ShowInfo(string message);

        /// <summary>
        /// 오류 메시지 표시.
        /// </summary>
        void ShowError(string message);

        /// <summary>
        /// 인증 버튼 텍스트를 변경한다.
        /// 예: 등록, 제거
        /// </summary>
        void SetAuthButtonText(string text);

        /// <summary>
        /// 확인 메시지를 표시하고 사용자 선택 결과를 반환한다.
        /// </summary>
        bool Confirm(string message);

        /// <summary>
        /// 인증키 입력 가능 여부를 변경한다.
        /// 
        /// 미등록 상태에서는 인증키를 입력해야 하므로 true,
        /// 등록 상태에서는 제거만 가능해야 하므로 false로 설정한다.
        /// </summary>
        /// <param name="enabled">
        /// true: 인증키 입력 가능
        /// false: 인증키 입력 불가
        /// </param>
        void SetLicenseKeyInputEnabled(bool enabled);

    }
}