# NKit 2
NKit 2 is a multipurpose disc image processor library with command line and GUI frontends available.

The goal of NKit is to help to centralise various apps that perform various game disc image manipulations for (currently) mainly video game consoles, like the Gamecube, Wii, Wii U, PS3 and more.

## Features
- Available in multiple OSes: Windows, Linux and OSX. And architectures: x86 and Arm in 64bit and 32bit.
- Conversion between formats like ISO / CISO / WBFS / RVZ / WUD / WUX / ZSO / JSO and more.
- Image fixing/unscrubbing such as restoring partitions, fixing region hacks, file order restore, correcting image size and more.
- Extracting all or specific files from images, even when the image is inside an archive.
- Encrypting and decrypting images.
- Verifying disc images against dat files and producing a scan file containing detailed structure, block and file system information.
- and more! See [the features page](https://github.com/Nanook/NKit/wiki/Features) for more information.

## Requirements
NKit requires the dotnet 6 runtime (or .NetFramework4.8 for the Windows 7 version).  
This is available as a package in many Linux distros or can be [downloaded from here](https://dotnet.microsoft.com/).

## Basic command line usage
Specify the task to perform and then list the image(s), folder(s) or even archive(s) to perform the ask on.

For example: To convert a selection of Wii images in various formats to Dolphin RVZ:
```
nkit -task convert image_1.wbfs image_2.iso folder_of_images archive_of_images.zip
```
See [the usage page](https://github.com/Nanook/NKit/wiki/Usage) for all arguments and details, and [the processing tasks page](https://github.com/Nanook/NKit/wiki/Processing-Tasks) for all currently supported tasks.

## Contact Us
Need some help? You can check our [wiki](https://github.com/Nanook/NKit/wiki), or you can ask on [Discord](https://discord.gg/ruzenmk369).
