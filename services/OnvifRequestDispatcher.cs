using System;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// ONVIF SOAP 요청을 분석하여 적절한 응답 생성 메서드로 분기하는 서비스.
    /// 
    /// 실제 HTTP 서버는 요청 본문을 문자열로 읽은 뒤 이 클래스에 전달한다.
    /// 이 클래스는 요청 XML에 포함된 ONVIF 동작명을 기준으로 응답 XML을 반환한다.
    /// </summary>
    public class OnvifRequestDispatcher
    {
        private readonly OnvifSoapResponseBuilder _responseBuilder;

        /// <summary>
        /// ONVIF 요청 분기 처리기를 생성한다.
        /// </summary>
        /// <param name="responseBuilder">ONVIF SOAP 응답 생성기.</param>
        public OnvifRequestDispatcher(OnvifSoapResponseBuilder responseBuilder)
        {
            if (responseBuilder == null)
                throw new ArgumentNullException("responseBuilder");

            _responseBuilder = responseBuilder;
        }

        /// <summary>
        /// ONVIF SOAP 요청 문자열을 분석하고 응답 XML을 반환한다.
        /// 
        /// StreamNo 기준:
        /// - 8080 요청 → Stream0 Profile만 응답
        /// - 8081 요청 → Stream1 Profile만 응답
        /// - 8082 요청 → Stream2 Profile만 응답
        /// </summary>
        /// <param name="requestBody">HTTP 요청 본문 SOAP XML.</param>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트.</param>
        /// <param name="onvifPort">요청을 받은 ONVIF HTTP 포트.</param>
        /// <param name="streamNo">현재 ONVIF 포트에 연결된 Stream 번호.</param>
        /// <returns>SOAP 응답 XML.</returns>
        public string Dispatch(string requestBody, AppConfig config, string host, int onvifPort, int streamNo)
        {
            string actionName = GetActionName(requestBody);

            if (string.Equals(actionName, "Empty", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildSoapFault("요청 본문이 비어 있습니다.");

            if (string.Equals(actionName, "GetDeviceInformation", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetDeviceInformationResponse(config, streamNo);

            if (string.Equals(actionName, "GetSystemDateAndTime", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetSystemDateAndTimeResponse();

            if (string.Equals(actionName, "GetServices", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetServicesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetScopes", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetScopesResponse();

            if (string.Equals(actionName, "GetCapabilities", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetCapabilitiesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetProfiles", StringComparison.OrdinalIgnoreCase))
            {
                return _responseBuilder.BuildGetProfilesResponse(config, streamNo);
            }
            if (string.Equals(actionName, "GetVideoSources", StringComparison.OrdinalIgnoreCase))
            {
                return _responseBuilder.BuildGetVideoSourcesResponse(config, streamNo);
            }

            if (string.Equals(actionName, "GetVideoSourceConfigurations", StringComparison.OrdinalIgnoreCase))
            {
                return _responseBuilder.BuildGetVideoSourceConfigurationsResponse(config, streamNo);
            }

            if (string.Equals(actionName, "GetVideoSourceConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                string configurationToken = ExtractVideoSourceConfigurationToken(requestBody, streamNo);

                return _responseBuilder.BuildGetVideoSourceConfigurationResponse(config,configurationToken,streamNo);
            }

            if (string.Equals(actionName,"GetVideoEncoderConfigurations",StringComparison.OrdinalIgnoreCase))
            {
                return _responseBuilder.BuildGetVideoEncoderConfigurationsResponse( config, streamNo);
            }

            if (string.Equals( actionName, "GetVideoEncoderConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                string configurationToken = ExtractConfigurationToken(requestBody, streamNo);

                return _responseBuilder.BuildGetVideoEncoderConfigurationResponse(config, configurationToken, streamNo);
            }
            if (string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * NVR이 ProfileToken을 전달하지 않는 경우도 있으므로,
                 * 현재 ONVIF 포트의 streamNo 기준 기본 ProfileToken을 사용한다.
                 */
                string profileToken = ExtractProfileToken(requestBody, streamNo);

                return _responseBuilder.BuildGetStreamUriResponse(
                    config,
                    host,
                    profileToken);
            }

            if (string.Equals(actionName, "GetVideoEncoderConfigurationOptions", StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * NVR이 GetProfiles에서 받은 VideoEncoderConfiguration token을 기준으로
                 * 해당 스트림의 인코딩 가능 옵션을 조회한다.
                 *
                 * 예:
                 * - video_encoder_0_main
                 * - video_encoder_0_sub
                 */
                string configurationToken = ExtractConfigurationToken(requestBody, streamNo);

                return _responseBuilder.BuildGetVideoEncoderConfigurationOptionsResponse(
                    config,
                    configurationToken,
                    streamNo);
            }

            if (string.Equals(actionName, "GetNetworkInterfaces", StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * NVR이 장비 등록 검증 과정에서 네트워크 인터페이스 정보를 조회한다.
                 * PC CAM은 실제 IP 카메라는 아니지만, ONVIF 장비로 인식되기 위한
                 * 최소 네트워크 정보를 반환한다.
                 */
                return _responseBuilder.BuildGetNetworkInterfacesResponse(config, host, streamNo);
            }
            return _responseBuilder.BuildSoapFault("지원하지 않는 ONVIF 요청입니다.");
        }

        /// <summary>
        /// 요청 본문에 특정 ONVIF 동작명이 포함되어 있는지 확인한다.
        /// 
        /// 네임스페이스 prefix가 tds, trt, 또는 임의 값일 수 있으므로
        /// 단순히 동작명 문자열 포함 여부로 판단한다.
        /// </summary>
        private bool ContainsAction(string requestBody, string actionName)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return false;

            return requestBody.IndexOf(actionName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ONVIF SOAP 요청 본문에서 요청 Action 이름을 판별한다.
        ///
        /// 주의:
        /// 이름이 서로 포함되는 Action은 반드시 긴 이름부터 확인해야 한다.
        ///
        /// 예:
        /// - GetVideoEncoderConfigurationOptions
        /// - GetVideoEncoderConfigurations
        /// - GetVideoEncoderConfiguration
        ///
        /// 짧은 이름을 먼저 검사하면 ConfigurationOptions 요청이
        /// Configuration 요청으로 잘못 판별될 수 있다.
        /// </summary>
        public string GetActionName(string requestBody)
        {
            /*
             * 요청 본문이 없으면 다른 Action 검사를 수행하지 않는다.
             */
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                return "Empty";
            }

            /*
             * VideoEncoder 관련 Action.
             *
             * Configuration이라는 문자열이 다른 Action 이름에도 포함되므로
             * 가장 긴 이름부터 검사해야 한다.
             */
            if (ContainsAction(requestBody, "GetVideoEncoderConfigurationOptions"))
            {
                return "GetVideoEncoderConfigurationOptions";
            }

            if (ContainsAction(requestBody, "GetVideoEncoderConfigurations"))
            {
                return "GetVideoEncoderConfigurations";
            }

            if (ContainsAction(requestBody, "GetVideoEncoderConfiguration"))
            {
                return "GetVideoEncoderConfiguration";
            }

            /*
             * VideoSource 관련 Action.
             *
             * Options와 Configurations를 singular Configuration보다
             * 먼저 검사한다.
             */
            if (ContainsAction(requestBody, "GetVideoSourceConfigurationOptions"))
            {
                return "GetVideoSourceConfigurationOptions";
            }

            if (ContainsAction(requestBody, "GetVideoSourceConfigurations"))
            {
                return "GetVideoSourceConfigurations";
            }

            if (ContainsAction(requestBody, "GetVideoSourceConfiguration"))
            {
                return "GetVideoSourceConfiguration";
            }

            if (ContainsAction(requestBody, "GetVideoSources"))
            {
                return "GetVideoSources";
            }

            /*
             * 기존 Device Service Action은 그대로 유지한다.
             */
            if (ContainsAction(requestBody, "GetDeviceInformation"))
            {
                return "GetDeviceInformation";
            }

            if (ContainsAction(requestBody, "GetSystemDateAndTime"))
            {
                return "GetSystemDateAndTime";
            }

            if (ContainsAction(requestBody, "GetServices"))
            {
                return "GetServices";
            }

            if (ContainsAction(requestBody, "GetScopes"))
            {
                return "GetScopes";
            }

            if (ContainsAction(requestBody, "GetCapabilities"))
            {
                return "GetCapabilities";
            }

            /*
             * 기존 Media Service Action도 그대로 유지한다.
             */
            if (ContainsAction(requestBody, "GetProfiles"))
            {
                return "GetProfiles";
            }

            if (ContainsAction(requestBody, "GetStreamUri"))
            {
                return "GetStreamUri";
            }

            if (ContainsAction(requestBody, "GetNetworkInterfaces"))
            {
                return "GetNetworkInterfaces";
            }

            return "Unknown";
        }

        /// <summary>
        /// GetStreamUri 요청 본문에서 ProfileToken 값을 추출한다.
        /// 
        /// ProfileToken을 찾지 못하면 현재 ONVIF 포트의 streamNo 기준으로
        /// profile_{streamNo}_main을 반환한다.
        /// 
        /// 예:
        /// - 8080 요청에서 토큰 없음 → profile_0_main
        /// - 8081 요청에서 토큰 없음 → profile_1_main
        /// </summary>
        /// <param name="requestBody">
        /// HTTP 요청 본문 SOAP XML.
        /// </param>
        /// <param name="streamNo">
        /// 현재 ONVIF 포트에 연결된 Stream 번호.
        /// </param>
        /// <returns>
        /// ProfileToken 값.
        /// </returns>
        private string ExtractProfileToken(
            string requestBody,
            int streamNo)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "profile_" + streamNo + "_main";

            string token = ExtractXmlElementValue(requestBody, "ProfileToken");

            if (string.IsNullOrWhiteSpace(token))
                return "profile_" + streamNo + "_main";

            return token.Trim();
        }

        /// <summary>
        /// GetVideoEncoderConfigurationOptions 요청 본문에서 ConfigurationToken 값을 추출한다.
        /// 
        /// NVR은 보통 GetProfiles 응답에 포함된 VideoEncoderConfiguration token을
        /// ConfigurationToken으로 다시 전달한다.
        /// 
        /// 예:
        /// - video_encoder_0_main
        /// - video_encoder_0_sub
        /// 
        /// 토큰을 찾지 못하면 현재 ONVIF 포트의 streamNo 기준 Main encoder token을 반환한다.
        /// </summary>
        /// <param name="requestBody">HTTP 요청 본문 SOAP XML.</param>
        /// <param name="streamNo">현재 ONVIF 포트에 연결된 Stream 번호.</param>
        /// <returns>VideoEncoderConfiguration token 값.</returns>
        private string ExtractConfigurationToken(
            string requestBody,
            int streamNo)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "video_encoder_" + streamNo + "_main";

            string token = ExtractXmlElementValue(requestBody, "ConfigurationToken");

            if (string.IsNullOrWhiteSpace(token))
                return "video_encoder_" + streamNo + "_main";

            return token.Trim();
        }

        /// <summary>
        /// ONVIF GetVideoSourceConfiguration 요청에서
        /// ConfigurationToken 값을 추출한다.
        ///
        /// 요청에 토큰이 없으면 현재 ONVIF 포트에 해당하는
        /// StreamNo 기준 기본 토큰을 반환한다.
        ///
        /// 토큰 형식:
        /// - video_source_0
        /// - video_source_1
        /// - video_source_2
        /// </summary>
        /// <param name="requestBody">
        /// NVR에서 전달한 ONVIF SOAP 요청 본문.
        /// </param>
        /// <param name="streamNo">
        /// 현재 ONVIF 서비스 포트에 연결된 스트림 번호.
        /// </param>
        /// <returns>
        /// 요청에서 추출한 VideoSourceConfiguration 토큰.
        /// 토큰이 없으면 video_source_{streamNo}.
        /// </returns>
        private string ExtractVideoSourceConfigurationToken(
            string requestBody,
            int streamNo)
        {
            /*
             * 요청 본문이 없으면 현재 스트림의 기본 토큰을 사용한다.
             */
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                return "video_source_" + streamNo;
            }

            /*
             * ONVIF 요청의 ConfigurationToken 요소를 찾는다.
             *
             * 예:
             * <trt:ConfigurationToken>
             *     video_source_0
             * </trt:ConfigurationToken>
             */
            string token =
                ExtractXmlElementValue(
                    requestBody,
                    "ConfigurationToken");

            /*
             * 일부 NVR은 ConfigurationToken을 전달하지 않을 수 있다.
             * 이 경우 현재 ONVIF 포트의 StreamNo 기준 토큰을 사용한다.
             */
            if (string.IsNullOrWhiteSpace(token))
            {
                return "video_source_" + streamNo;
            }

            return token.Trim();
        }

        /// <summary>
        /// XML 문자열에서 특정 태그의 값을 단순 문자열 방식으로 추출한다.
        /// 
        /// namespace prefix가 있을 수 있으므로 아래 두 형태를 모두 고려한다.
        /// - <ProfileToken>...</ProfileToken>
        /// - <trt:ProfileToken>...</trt:ProfileToken>
        /// </summary>
        /// <param name="xml">
        /// XML 문자열.
        /// </param>
        /// <param name="elementName">
        /// 찾을 태그명.
        /// </param>
        /// <returns>
        /// 태그 내부 값.
        /// 찾지 못하면 빈 문자열.
        /// </returns>
        private string ExtractXmlElementValue(string xml, string elementName)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return "";

            if (string.IsNullOrWhiteSpace(elementName))
                return "";

            /*
             * 1. prefix 없는 형태 우선 검색.
             */
            string directStartTag = "<" + elementName + ">";
            string directEndTag = "</" + elementName + ">";

            string value = ExtractBetween(xml, directStartTag, directEndTag);

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            /*
             * 2. namespace prefix가 있는 형태 검색.
             * 예: <trt:ProfileToken>...</trt:ProfileToken>
             */
            string startPattern = ":" + elementName + ">";

            int startIndex = xml.IndexOf(startPattern, StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            startIndex = startIndex + startPattern.Length;

            int endIndex = xml.IndexOf("</", startIndex, StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return "";

            return xml.Substring(startIndex, endIndex - startIndex);
        }

        /// <summary>
        /// 문자열에서 시작 태그와 종료 태그 사이의 값을 추출한다.
        /// </summary>
        private string ExtractBetween(string source, string startText, string endText)
        {
            int startIndex = source.IndexOf(startText, StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            startIndex += startText.Length;

            int endIndex = source.IndexOf(endText, startIndex, StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return "";

            return source.Substring(startIndex, endIndex - startIndex);
        }
    }
}