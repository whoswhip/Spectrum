using ImGuiNET;
using Spectrum.Input;
using System.Numerics;

namespace Spectrum
{
    partial class Renderer
    {
        private bool _showMovementPreview = false;
        private Point _previewStartPoint = new(100, 100);
        private Point _previewEndPoint = new(400, 300);
        private List<(Point point, double speed)> _previewPath = [];
        private int UpdatePreviewInterval = 10; // ms
        private DateTime _lastPreviewUpdate = DateTime.MinValue;
        private bool _isDragging = false;
        private bool _isMovingStart = false;

        private void RenderMovementPreview()
        {
            if (!_showMovementPreview)
                return;

            var config = mainConfig.Data;

            ImGui.SetNextWindowSize(new Vector2(500, 500), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(screenSize.width / 2 - 300, screenSize.height / 2 - 250), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("Movement Path Preview", ref _showMovementPreview, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.End();
                return;
            }

            ImGui.TextUnformatted("Movement Path Preview");
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 14);
            if (ImGui.Button("X", new Vector2(22, 22)))
            {
                _showMovementPreview = false;
                ImGui.End();
                return;
            }

            if ((DateTime.Now - _lastPreviewUpdate).TotalMilliseconds > UpdatePreviewInterval)
            {
                _lastPreviewUpdate = DateTime.Now;
                _previewPath.Clear();

                MovementPaths.ResetWindMouse();
                double progress = 0.0;
                double increment = config.Sensitivity * 0.02;
                Point current = _previewStartPoint;
                int steps = 0;
                int maxSteps = 10000;

                while (true)
                {
                    double distance = Math.Sqrt(Math.Pow(_previewEndPoint.X - current.X, 2) + Math.Pow(_previewEndPoint.Y - current.Y, 2));
                    if (distance < config.WindMouseTargetArea || steps++ >= maxSteps)
                        break;
                    Point nextPoint = config.AimMovementType switch
                    {
                        MovementType.Linear => MovementPaths.LinearInterpolation(current, _previewEndPoint, progress),
                        MovementType.CubicBezier => MovementPaths.CubicBezierCurvedMovement(current, _previewEndPoint, progress),
                        MovementType.Adaptive => MovementPaths.AdaptiveMovement(current, _previewEndPoint, progress),
                        MovementType.QuadraticBezier => MovementPaths.CurvedMovement(current, _previewEndPoint, progress),
                        MovementType.PerlinNoise => MovementPaths.PerlinNoiseMovement(current, _previewEndPoint, progress),
                        MovementType.WindMouse => MovementPaths.WindMouse(current, _previewEndPoint,
                            config.WindMouseGravity,
                            config.WindMouseWind,
                            config.WindMouseMaxStep,
                            config.WindMouseTargetArea,
                            config.Sensitivity,
                            config.WindMouseOvershoot,
                            false),
                        _ => MovementPaths.LinearInterpolation(current, _previewEndPoint, progress),
                    };
                    if (config.EmaSmoothening)
                        nextPoint = MovementPaths.EmaSmoothing(current, nextPoint, config.EmaSmootheningFactor);

                    double speed = Math.Sqrt(Math.Pow(nextPoint.X - current.X, 2) + Math.Pow(nextPoint.Y - current.Y, 2));
                    _previewPath.Add((nextPoint, speed));
                    current = nextPoint;
                    progress += increment;
                }
            }

            var drawList = ImGui.GetWindowDrawList();
            Vector2 canvasPos = ImGui.GetCursorScreenPos();
            canvasPos.Y += 5;
            Vector2 canvasSize = ImGui.GetContentRegionAvail();
            Vector2 size = new Vector2(canvasSize.X, canvasSize.Y - 60);
            drawList.AddRectFilled(canvasPos, size + canvasPos, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1.0f)), 3f);
            drawList.AddRect(canvasPos, size + canvasPos, ImGui.GetColorU32(new Vector4(0.17f, 0.17f, 0.20f, 1.0f)), 3f);

            if (_previewPath.Count > 1)
            {
                double maxSpeed = _previewPath.Max(p => p.speed);
                for (int i = 1; i < _previewPath.Count; i++)
                {
                    var (prevPoint, _) = _previewPath[i - 1];
                    var (currPoint, speed) = _previewPath[i];
                    Vector2 p1 = new Vector2(canvasPos.X + prevPoint.X, canvasPos.Y + prevPoint.Y);
                    Vector2 p2 = new Vector2(canvasPos.X + currPoint.X, canvasPos.Y + currPoint.Y);
                    Vector4 color = speed < maxSpeed / 2 ?
                        Vector4.Lerp(new Vector4(0, 1, 0, 1), new Vector4(1, 1, 0, 1), (float)(speed / (maxSpeed / 2))) :
                        Vector4.Lerp(new Vector4(1, 1, 0, 1), new Vector4(1, 0, 0, 1), (float)((speed - (maxSpeed / 2)) / (maxSpeed / 2)));
                    drawList.AddLine(p1, p2, ImGui.GetColorU32(color), 2.0f);
                }
            }

            drawList.AddCircleFilled(new Vector2(canvasPos.X + _previewStartPoint.X, canvasPos.Y + _previewStartPoint.Y), 5.0f, ImGui.GetColorU32(new Vector4(0, 1, 0, 1)));
            drawList.AddCircleFilled(new Vector2(canvasPos.X + _previewEndPoint.X, canvasPos.Y + _previewEndPoint.Y), 5.0f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));

            ImGui.InvisibleButton("canvas", size);
            var io = ImGui.GetIO();
            var mousePos = io.MousePos;
            bool isMouseInCanvas = mousePos.X >= canvasPos.X && mousePos.X <= canvasPos.X + size.X &&
                                   mousePos.Y >= canvasPos.Y && mousePos.Y <= canvasPos.Y + size.Y;

            bool isMouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool isMouseClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool isMouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

            if (!_isDragging && isMouseInCanvas && isMouseClicked)
            {
                Vector2 localMousePos = mousePos - canvasPos;
                float distToStart = Vector2.Distance(localMousePos, new Vector2(_previewStartPoint.X, _previewStartPoint.Y));
                float distToEnd = Vector2.Distance(localMousePos, new Vector2(_previewEndPoint.X, _previewEndPoint.Y));
                if (distToStart < 10.0f)
                {
                    _isMovingStart = true;
                    _isDragging = true;
                }
                else if (distToEnd < 10.0f)
                {
                    _isMovingStart = false;
                    _isDragging = true;
                }
                else
                {
                    _isDragging = false;
                    _isMovingStart = false;
                }
            }

            if (_isDragging && isMouseDown)
            {
                Vector2 localMousePos = mousePos - canvasPos;
                if (_isMovingStart)
                {
                    _previewStartPoint = new Point((int)localMousePos.X, (int)localMousePos.Y);
                }
                else
                {
                    _previewEndPoint = new Point((int)localMousePos.X, (int)localMousePos.Y);
                }
            }

            if (_isDragging && isMouseReleased)
            {
                _isDragging = false;
            }

            ImGui.Dummy(new(ImGui.GetContentRegionAvail().X, 10));
            int interval = UpdatePreviewInterval;
            if (ImGuiExtensions.SliderFill("Update Interval (ms)", ref interval, 1, 100))
                UpdatePreviewInterval = interval;

            ImGui.End();
        }
    }
}
