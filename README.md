# Spectrum
Spectrum is a semi-universal colorbot that uses C# and OpenCV for image processing.

## Features
- Extensive configuration file
- Dataset collection
- Multiple mouse movement paths

## Configuration
On first run, a default configuration file is created.
### Parameters
| Parameter     | Range/Options         | Default Value | Description                     |
|---------------|-----------------------|---------------|---------------------------------|
| `ImageWidth`  | 1 - ScreenSize        | 640           | Defines the width of the image |
| `ImageHeight` | 1 - ScreenSize        | 640           | Defines the height of the image|

| Parameter         | Range/Options | Default Value | Description                     |
|-------------------|---------------|---------------|---------------------------------|
| `YOffsetPercent`  | 0.0 - 1       | 0.8           | Sets the vertical offset       |
| `XOffsetPercent`  | 0.0 - 1       | 0.5           | Sets the horizontal offset     |

| Parameter          | Range/Options                 | Default Value | Description                                             |
|--------------------|-------------------------------|---------------|---------------------------------------------------------|
| `EnableAim`        | true or false                | true          | Enables or disables aim functionality                   |
| `ClosestToMouse`   | true or false                | true          | Determines aiming at the closest target to the mouse    |
| `Keybind`          | 1 - 254                      | 6             | Virtual key codes are used for keybinding              |
| `Sensitivity`      | 0.1 - 2                      | 0.5           | Defines the sensitivity of the aim                     |
| `AimMovementType`  | CubicBezier, Linear, Adaptive| CubicBezier   | Specifies the type of aim movement                     |

| Parameter             | Range/Options | Default Value | Description                     |
|-----------------------|---------------|---------------|---------------------------------|
| `ShowDetectionWindow` | true or false | true          | Displays the detection window   |

| Parameter                 | Range/Options | Default Value | Description                                   |
|---------------------------|---------------|---------------|-----------------------------------------------|
| `CollectData`             | true or false | false         | Enables or disables data collection          |
| `AutoLabel`               | true or false | false         | Depends on `CollectData`; auto-labels data   |
| `BackgroundImageInterval` | 1 - 100       | 10            | Sets interval for background image collection|

| Parameter   | Sub-Parameter | Range/Options | Default Value | Description                     |
|-------------|---------------|---------------|---------------|---------------------------------|
| **UpperHSV**| `Val0`        | 0 - 255       | 150           | Hue                             |
|             | `Val1`        | 0 - 255       | 255           | Saturation                      |
|             | `Val2`        | 0 - 255       | 229           | Value                           |
|             | `Val3`        | 0             | 0             |                                 |
| **LowerHSV**| `Val0`        | 0 - 255       | 150           | Hue                             |
|             | `Val1`        | 0 - 255       | 255           | Saturation                      |
|             | `Val2`        | 0 - 255       | 229           | Value                           |
|             | `Val3`        | 0             | 0             |                                 |

## Dataset Collection
When `CollectData` and `AutoLabel` are enabled, Spectrum automatically generates:
- **Images**: Captured screenshots saved to `dataset/images`
- **Labels**: YOLO format annotation files saved to `dataset/labels`
- **Background Images**: Screenshots without any labels, helps training balance
This feature is very useful for creating training datasets for machine learning models.

## License

See [LICENSE.txt](LICENSE.txt) for license information.

## Disclaimer

This software is intended for educational and research purposes. Users are responsible for ensuring compliance with applicable laws and terms of service when using this software. The developers assume no responsibility for misuse of this application.

## Support

For issues, questions, or feature requests, please open an issue on the project repository.
