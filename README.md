# Virtual Glasses Try-On using Unity AR

A Unity-based augmented reality prototype for virtual eyeglasses try-on using face tracking.

## Project Overview

This project was developed as a fourth-year university project.
It allows users to virtually try on eyeglasses using a mobile AR experience. The system tracks the user's face and places a 3D glasses model on it, updating the glasses position, rotation, and scale according to face movement.

The project focuses on combining Unity AR features with runtime model loading and mobile interaction to create an interactive virtual try-on experience.

## Features

* Real-time AR face tracking using Unity AR Foundation.
* Virtual eyeglasses placement on the user's face.
* Runtime loading of 3D glasses models.
* Support for GLB/GLTF model loading using GLTFast.
* Front camera selection for mobile AR.
* Face mesh anchoring using ARFace vertices.
* Dynamic glasses positioning, scaling, and rotation.
* Glasses arm visibility and rotation control based on face tilt.
* Screenshot capture for saving try-on results.
* Android integration support for receiving model URLs from another app.
* Mobile debug logging UI.

## Main Scripts

* `IntegratedARGlassesLoader.cs`: Loads and attaches glasses models to the tracked face.
* `LoadModelFromIntent.cs`: Receives a model URL from an Android intent and loads it in Unity.
* `glassScript.cs`: Handles model separation, mesh parts, and glasses arm manipulation.
* `PointsScript.cs`: Positions glasses according to ARFace mesh points.
* `FrontCameraSelector.cs`: Selects the front camera for AR usage.
* `PhotoCapture.cs`: Captures try-on screenshots.
* `MobileDebugLogger.cs`: Displays runtime debug logs on mobile.

## Technologies Used

* Unity
* C#
* AR Foundation
* ARCore
* GLTFast
* Android
* NativeGallery

## Project Structure

```txt
virtual-glasses-try-on-unity-ar/
  Assets/
  Packages/
  ProjectSettings/
  README.md
  .gitignore
```

## How to Run

1. Clone the repository.

```bash
git clone https://github.com/mina360/virtual-glasses-try-on-unity-ar.git
```

2. Open the project using Unity Hub.

3. Use the Unity version listed in:

```txt
ProjectSettings/ProjectVersion.txt
```

4. Allow Unity to restore the required packages from the Unity Package Manager.

5. Open the main AR scene from the `Assets` folder.

6. Build and run the project on an Android device that supports ARCore.

## Demo APK

A demo Android APK is available in the repository Releases section.

The APK is provided as a demo build for testing the virtual glasses try-on experience on supported Android devices.

## Notes

* This is an academic AR prototype, not a production-ready commercial application.
* The project is intended for Android mobile AR usage.
* APK build files are not included in the main repository files to keep the repository clean.
* Demo builds are provided separately through GitHub Releases.
* The project requires an Android device that supports ARCore.
