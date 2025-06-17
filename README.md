# Spectrum
Spectrum is a semi-universal colorbot that uses C# and OpenCV for image processing.

## Features
- Extensive configuration file
- Dataset collection
- Multiple mouse movement paths

## Configuration
On first run, a default configuration file is created.
### Parameters
#### `ImageSettings`
`ImageWidth`: 1 - ScreenSize (Default: 640)

`ImageHeight`: 1 - ScreenSize (Default: 640)
#### `OffsetSettings 
`YOffsetPercent`: 0.0 - 1 (Default: 0.8)

`XOffsetPercent`: 0.0 - 1 (Default: 0.5)
#### `AimSettings`
`EnableAim`: true or false (Default: true)

`ClosestToMouse`: true or false (Default: true)

`Keybind`: 1 - 254 (Default: 6), these are [virtual key codes](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes)

`Sensitivity`: 0.1 - 2 (Default: 0.5) 

`AimMovementType`: CubicBezier, Linear & Adaptive (Default: CubicBezier)

#### `DisplaySettings`
`ShowDetectionWindow`: true or false (Default: true)

#### `DataCollectionSettings`
`CollectData`: true or false (Default: false)

`AutoLabel`: true or false (Default: false), this is dependent on `CollectData`

`BackgroundImageInterval`: 1 - 100 (Default: 10)

#### `ColorSettings`
`UpperHSV`
- `Val0`: 0 - 255 (Default: 150) Hue
- `Val1`: 0 - 255 (Default: 255) Saturation
- `Val2`: 0 - 255 (Default: 229) Value
- `Val3`: 0 (Default: 0) ?

`LowerHSV`
- `Val0`: 0 - 255 (Default: 150) Hue
- `Val1`: 0 - 255 (Default: 255) Saturation
- `Val2`: 0 - 255 (Default: 229) Value
- `Val3`: 0 (Default: 0) ?

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
