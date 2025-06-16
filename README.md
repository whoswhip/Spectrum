# Spectrum

A real-time computer vision application for object detection and automated targeting using OpenCV and C#. Spectrum captures screen regions, detects objects based on HSV color filtering, and can automatically move the mouse cursor to target detected objects.

## Features

- **Real-time Screen Capture**: Captures specific screen regions for processing
- **HSV Color-based Detection**: Uses HSV color space filtering to detect objects
- **Automated Targeting**: Automatically moves mouse cursor to detected targets
- **Dataset Collection**: Automatically generates labeled datasets for machine learning
- **Configurable Parameters**: Extensive configuration options via JSON file
- **Visual Feedback**: Optional detection window showing real-time results
- **Hotkey Support**: Configurable keybinding for activation

## Requirements

- .NET 8.0 or later
- Windows operating system
- OpenCV 4.11.0+

## Installation

1. Clone the repository:
   ```powershell
   git clone https://github.com/whoswhip/Spectrum.git
   cd Spectrum
   ```

2. Build the project:
   ```powershell
   dotnet build
   ```

3. Run the application:
   ```powershell
   dotnet run --project Spectrum
   ```

## Configuration

The application uses a `config.json` file for configuration. On first run, a default configuration file will be created. You can modify the following parameters:

### Display Settings
- `ImageWidth`: Width of the capture region (default: 640)
- `ImageHeight`: Height of the capture region (default: 640)
- `ShowDetectionWindow`: Show real-time detection window (default: true)

### Detection Settings
- `UpperHSV`: Upper HSV color bounds for detection
- `LowerHSV`: Lower HSV color bounds for detection
- `YOffsetPercent`: Y-axis offset percentage for targeting (default: 0.8)
- `XOffsetPercent`: X-axis offset percentage for targeting (default: 0.5)

### Control Settings
- `EnableAim`: Enable automatic mouse movement (default: true)
- `ClosestToMouse`: Target closest object to mouse cursor (default: true)
- `Keybind`: Activation key code (default: 6 - mouse side button)
- `Sensitivity`: Mouse movement sensitivity (default: 0.5)

### Data Collection
- `CollectData`: Enable dataset collection (default: false)
- `AutoLabel`: Enable automatic labeling for YOLO format (default: false)
- `BackgroundImageInterval`: Interval for background image collection (default: 10)

### Example Configuration

```json
{
  "ImageWidth": 640,
  "ImageHeight": 640,
  "YOffsetPercent": 0.8,
  "XOffsetPercent": 0.5,
  "EnableAim": true,
  "ClosestToMouse": true,
  "Keybind": 6,
  "Sensitivity": 0.1,
  "ShowDetectionWindow": true,
  "CollectData": false,
  "AutoLabel": false,
  "BackgroundImageInterval": 10,
  "UpperHSV": {
    "Val0": 150.0,
    "Val1": 255.0,
    "Val2": 229.0,
    "Val3": 0.0
  },
  "LowerHSV": {
    "Val0": 150.0,
    "Val1": 255.0,
    "Val2": 229.0,
    "Val3": 0.0
  }
}
```

## Usage

1. **Start the application**: Run the executable or use `dotnet run`
2. **Configure detection**: Adjust HSV values in `config.json` for your target objects
3. **Activate detection**: Press the configured keybind (default: mouse side button)
4. **Real-time targeting**: The application will automatically detect and target objects
5. **Reload configuration**: Press F5 or R to reload configuration without restarting

### Keyboard Controls

- **Escape**: Exit the application
- **F5/R**: Reload configuration from file
- **Configured Keybind**: Activate detection and targeting

## Dataset Collection

When `CollectData` and `AutoLabel` are enabled, Spectrum automatically generates:

- **Images**: Captured screenshots saved to `dataset/images/`
- **Labels**: YOLO format annotation files saved to `dataset/labels/`
- **Background Images**: Non-detection samples for training balance

This feature is useful for creating training datasets for machine learning models.

## Technical Details

### Dependencies

- **OpenCvSharp4**: OpenCV bindings for .NET
- **Newtonsoft.Json**: JSON configuration handling
- **System.Drawing.Common**: Screen capture and image processing

### Architecture

- **Program.cs**: Main application loop and screen capture
- **Config.cs**: Configuration management and validation
- **AutoLabeling.cs**: Dataset generation and YOLO labeling
- **InputManager.cs**: Mouse input simulation

### Detection Pipeline

1. **Screen Capture**: Captures specified screen region
2. **Color Space Conversion**: Converts BGR to HSV
3. **Color Filtering**: Applies HSV range mask
4. **Morphological Operations**: Dilates image to fill gaps
5. **Contour Detection**: Finds object boundaries
6. **Filtering**: Removes small contours (area < 100)
7. **Target Selection**: Chooses closest target to reference point
8. **Mouse Movement**: Moves cursor to calculated target position

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## License

See LICENSE.txt for license information.

## Disclaimer

This software is intended for educational and research purposes. Users are responsible for ensuring compliance with applicable laws and terms of service when using this software. The developers assume no responsibility for misuse of this application.

## Support

For issues, questions, or feature requests, please open an issue on the project repository.
