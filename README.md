# Spectrum
Spectrum is a semi-universal colorbot that uses C# and OpenCV for image processing.
<img width="600" height="502" alt="Spectrum Menu" src="https://upld.zip/d9Z8Evjm.png" />
## Features
- Extensive configuration file
- Dataset collection
- Multiple mouse movement paths
- IMGui Menu
- FOV Overlay
- Detection Overlay

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
