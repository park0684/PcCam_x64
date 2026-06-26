using System;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 투명 BGRA 오버레이 프레임을 생성한다.
    ///
    /// 1차 POC 정책:
    /// - 가장 최근 클릭 위치 하나만 표시
    /// - 터치와 마우스를 구분하지 않음
    /// - 원형 링과 중심점을 표시
    /// - 500ms 유지 후 200ms 동안 페이드아웃
    /// - 프레임 버퍼를 매번 새로 만들지 않고 재사용
    /// - 이전 포인터 영역만 초기화
    /// </summary>
    public sealed class TouchOverlayFrameRenderer
    {
        private readonly object _syncLock = new object();

        private readonly int _width;
        private readonly int _height;
        private readonly int _diameter;
        private readonly int _visibleMilliseconds;
        private readonly int _fadeMilliseconds;
        private readonly byte[] _frameBuffer;

        private bool _hasPointer;
        private int _pointerX;
        private int _pointerY;
        private DateTime _pointerOccurredAtUtc;

        private bool _hasDirtyRegion;
        private int _dirtyLeft;
        private int _dirtyTop;
        private int _dirtyWidth;
        private int _dirtyHeight;

        public TouchOverlayFrameRenderer(
            int width,
            int height,
            int diameter,
            int visibleMilliseconds,
            int fadeMilliseconds)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");

            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");

            if (diameter < 20)
                throw new ArgumentOutOfRangeException("diameter");

            if (visibleMilliseconds < 0)
                throw new ArgumentOutOfRangeException("visibleMilliseconds");

            if (fadeMilliseconds < 0)
                throw new ArgumentOutOfRangeException("fadeMilliseconds");

            long bufferLength =
                (long)width *
                (long)height *
                4L;

            if (bufferLength > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "터치 오버레이 프레임 버퍼가 너무 큽니다.");
            }

            _width = width;
            _height = height;
            _diameter = diameter;
            _visibleMilliseconds = visibleMilliseconds;
            _fadeMilliseconds = fadeMilliseconds;
            _frameBuffer = new byte[(int)bufferLength];
        }

        /// <summary>
        /// 새로운 포인터 위치를 갱신한다.
        /// 좌표는 현재 스트림 모니터의 로컬 좌표이다.
        /// </summary>
        public void ShowPointer(
            int x,
            int y)
        {
            lock (_syncLock)
            {
                _pointerX = x;
                _pointerY = y;
                _pointerOccurredAtUtc = DateTime.UtcNow;
                _hasPointer = true;
            }
        }

        /// <summary>
        /// 현재 시각 기준 오버레이 프레임을 렌더링한다.
        /// 반환 배열은 내부 재사용 버퍼이므로 호출자가 보관하거나 수정하면 안 된다.
        /// </summary>
        public byte[] RenderFrame(
            DateTime utcNow)
        {
            ClearPreviousDirtyRegion();

            bool hasPointer;
            int pointerX;
            int pointerY;
            DateTime occurredAtUtc;

            lock (_syncLock)
            {
                hasPointer = _hasPointer;
                pointerX = _pointerX;
                pointerY = _pointerY;
                occurredAtUtc = _pointerOccurredAtUtc;
            }

            if (!hasPointer)
                return _frameBuffer;

            double elapsedMilliseconds =
                (utcNow - occurredAtUtc).TotalMilliseconds;

            if (elapsedMilliseconds < 0)
                elapsedMilliseconds = 0;

            int totalMilliseconds =
                _visibleMilliseconds +
                _fadeMilliseconds;

            if (elapsedMilliseconds >= totalMilliseconds)
                return _frameBuffer;

            double alphaRatio = 1.0;

            if (_fadeMilliseconds > 0 &&
                elapsedMilliseconds > _visibleMilliseconds)
            {
                alphaRatio =
                    1.0 -
                    ((elapsedMilliseconds - _visibleMilliseconds) /
                     _fadeMilliseconds);
            }

            if (alphaRatio < 0)
                alphaRatio = 0;

            if (alphaRatio > 1)
                alphaRatio = 1;

            byte alpha =
                (byte)Math.Max(
                    0,
                    Math.Min(
                        255,
                        (int)Math.Round(220.0 * alphaRatio)));

            DrawPointer(
                pointerX,
                pointerY,
                alpha);

            return _frameBuffer;
        }

        private void DrawPointer(
            int centerX,
            int centerY,
            byte alpha)
        {
            int outerRadius =
                Math.Max(10, _diameter / 2);

            int margin = 2;

            int left =
                Math.Max(
                    0,
                    centerX - outerRadius - margin);

            int top =
                Math.Max(
                    0,
                    centerY - outerRadius - margin);

            int right =
                Math.Min(
                    _width - 1,
                    centerX + outerRadius + margin);

            int bottom =
                Math.Min(
                    _height - 1,
                    centerY + outerRadius + margin);

            if (right < left || bottom < top)
                return;

            /*
             * BGRA 순서:
             * B=255, G=0, R=255 → 자주색
             *
             * 알파값도 255로 고정하여
             * 투명도 문제 여부를 확실하게 확인한다.
             */
            DrawFilledCircle(
                centerX,
                centerY,
                outerRadius,
                255,
                0,
                255,
                255);

            _dirtyLeft = left;
            _dirtyTop = top;
            _dirtyWidth = right - left + 1;
            _dirtyHeight = bottom - top + 1;
            _hasDirtyRegion = true;
        }

        private void DrawRing(
            int centerX,
            int centerY,
            int radius,
            int thickness,
            byte blue,
            byte green,
            byte red,
            byte alpha)
        {
            int innerRadius =
                Math.Max(
                    0,
                    radius - thickness);

            int outerSquared =
                radius * radius;

            int innerSquared =
                innerRadius * innerRadius;

            int left =
                Math.Max(
                    0,
                    centerX - radius);

            int top =
                Math.Max(
                    0,
                    centerY - radius);

            int right =
                Math.Min(
                    _width - 1,
                    centerX + radius);

            int bottom =
                Math.Min(
                    _height - 1,
                    centerY + radius);

            for (int y = top; y <= bottom; y++)
            {
                int deltaY = y - centerY;
                int deltaYSquared = deltaY * deltaY;

                for (int x = left; x <= right; x++)
                {
                    int deltaX = x - centerX;
                    int distanceSquared =
                        (deltaX * deltaX) +
                        deltaYSquared;

                    if (distanceSquared > outerSquared ||
                        distanceSquared < innerSquared)
                    {
                        continue;
                    }

                    SetPixel(
                        x,
                        y,
                        blue,
                        green,
                        red,
                        alpha);
                }
            }
        }

        private void DrawFilledCircle(
            int centerX,
            int centerY,
            int radius,
            byte blue,
            byte green,
            byte red,
            byte alpha)
        {
            int radiusSquared =
                radius * radius;

            int left =
                Math.Max(
                    0,
                    centerX - radius);

            int top =
                Math.Max(
                    0,
                    centerY - radius);

            int right =
                Math.Min(
                    _width - 1,
                    centerX + radius);

            int bottom =
                Math.Min(
                    _height - 1,
                    centerY + radius);

            for (int y = top; y <= bottom; y++)
            {
                int deltaY = y - centerY;
                int deltaYSquared = deltaY * deltaY;

                for (int x = left; x <= right; x++)
                {
                    int deltaX = x - centerX;
                    int distanceSquared =
                        (deltaX * deltaX) +
                        deltaYSquared;

                    if (distanceSquared > radiusSquared)
                        continue;

                    SetPixel(
                        x,
                        y,
                        blue,
                        green,
                        red,
                        alpha);
                }
            }
        }

        private void SetPixel(
            int x,
            int y,
            byte blue,
            byte green,
            byte red,
            byte alpha)
        {
            int index =
                ((y * _width) + x) * 4;

            _frameBuffer[index] = blue;
            _frameBuffer[index + 1] = green;
            _frameBuffer[index + 2] = red;
            _frameBuffer[index + 3] = alpha;
        }

        private void ClearPreviousDirtyRegion()
        {
            if (!_hasDirtyRegion)
                return;

            int rowByteCount =
                _dirtyWidth * 4;

            for (int y = 0; y < _dirtyHeight; y++)
            {
                int rowStart =
                    (((_dirtyTop + y) * _width) +
                     _dirtyLeft) *
                    4;

                Array.Clear(
                    _frameBuffer,
                    rowStart,
                    rowByteCount);
            }

            _hasDirtyRegion = false;
        }
    }
}