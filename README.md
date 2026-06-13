# virtual-glasses-try-on-unity-ar
# Virtual Glasses Try-On using Unity AR

A Unity-based augmented reality project for virtual eyeglasses try-on using face tracking.

## Project Overview

This project was developed as a fourth-year university project.  
It allows users to virtually try on eyeglasses using AR face tracking. The system places a 3D glasses model on the user's face and updates its position according to face movement.

## Features

- Real-time AR face tracking using Unity AR Foundation.
- Virtual eyeglasses placement on the face.
- Runtime loading of 3D glasses models.
- Support for GLB/GLTF model loading using GLTFast.
- Front camera selection for mobile AR.
- Face mesh anchoring using ARFace vertices.
- Dynamic glasses positioning, scaling, and rotation.
- Arm visibility and rotation control based on face tilt.
- Screenshot capture for saving try-on results.
- Android integration support for receiving model URLs from another app.
- Mobile debug logging UI.

## Main Scripts

- `IntegratedARGlassesLoader.cs`: Loads and attaches glasses models to the tracked face.
- `LoadModelFromIntent.cs`: Receives a model URL from Android intent and loads it in Unity.
- `glassScript.cs`: Handles model separation, mesh parts, and glasses arm manipulation.
- `PointsScript.cs`: Positions glasses according to ARFace mesh points.
- `FrontCameraSelector.cs`: Selects the front camera for AR usage.
- `PhotoCapture.cs`: Captures try-on screenshots.
- `MobileDebugLogger.cs`: Displays runtime debug logs on mobile.

## Technologies Used

- Unity
- C#
- AR Foundation
- ARCore
- GLTFast
- Android
- NativeGallery

## Project Structure

```txt
virtual-glasses-try-on-unity-ar/
  Assets/
  Packages/
  ProjectSettings/
  README.md
  .gitignore