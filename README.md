# VR Full-Body Tracking over OSC

![Full-body tracking driving an avatar in real time](https://jon-gear.github.io/assets/images/internship-1/fbt.gif)

A Unity full-body tracking solution for standalone VR (Meta Quest). Real body trackers drive a humanoid avatar in real time.
Tracker data arrives over **OSC** using the [VRChat OSC Trackers](https://docs.vrchat.com/docs/osc-trackers) protocol,
is decoded with [OscCore](https://github.com/vrchat/OscCore), and is applied with Unity's built-in **Animator IK**
(no paid IK package needed).

It was built during my internship at SportsImproVR to bring lower-body movement (stepping, crouching, jumping, leg positioning)
into VR tennis training. Write-up: [Internship at SportsImproVR](https://jon-gear.github.io/work/internship-1-short/).


## How it works

The headset only knows about the head and hands. This project adds the rest of the body:

![The pipeline, with the part this project builds highlighted](https://jon-gear.github.io/assets/images/internship-1/diagrams/what-actually-happened.png)

1. **HaritoraX2** trackers connect to a smartphone over Bluetooth.
2. **Shiftall VR Manager** on the phone handles calibration and pose estimation, and turns the raw IMU data into a body pose.
3. The phone sends the tracker positions and rotations over Wi-Fi as **OSC**.
4. This project's Unity app on the Quest **receives** the OSC messages, turns them into tracker targets, and applies them to the avatar with IK.

The Unity side is the missing piece: an OSC receiver plus the mapping to IK. Bluetooth, calibration and pose estimation are left to Shiftall VR Manager.

```
tracker app --UDP/OSC--> OscServer (OscCore) --> OscFullBodyIKController --> Animator IK --> avatar
                                                        ^
                                         headset + controllers (XR rig or simulated)
```

This mirrors the pipeline VRChat uses, which is how the hardware was verified first:

![VRChat's tracking pipeline](https://jon-gear.github.io/assets/images/internship-1/diagrams/vr-chat-diagram.png)

For reference, the same trackers working in VRChat:

![HaritoraX2 trackers working in VRChat](https://jon-gear.github.io/assets/images/internship-1/media/image37.gif)


Any sender that speaks the VRChat OSC Trackers protocol should work the same way, not only HaritoraX2.

## Why HaritoraX2?

Before building this I compared the main kinds of full-body tracking for a standalone Quest sports app. XSens DOT trackers were tried first, but they only give reliable **orientation**: their position drifts, and they have no pose-estimation layer, so every rotation would have needed custom skeleton code tied to one vendor.

| Technology | Examples | Verdict for a portable sports-training app |
|---|---|---|
| IMU | SlimeVR, HaritoraX2, Sony mocopi | Cheap, portable, no base stations, no occlusion. Drifts over time and needs calibration. |
| Electromagnetic | FluxPose | Promising absolute positioning, but not production-proven at the time. |
| Lighthouse | Vive Tracker, Tundra Tracker | Most accurate, but expensive, needs base stations, not portable, can be occluded. |
| Camera | Kinect with Amethyst | Cheapest and no trackers to wear, but poor with fast movement, turning and leaving the view. |

HaritoraX2 had the best balance. Its **Shiftall VR Manager** already does pose estimation and outputs a skeleton over **OSC**, so the Unity side only has to receive and apply it.

## Requirements

| | |
|---|---|
| Unity | 2022.3.62f2 (URP 14.0.12) |
| Packages | XR Interaction Toolkit 3.1.2, OpenXR 1.14.3, Input System 1.14.0, TextMeshPro (all in `Packages/manifest.json`) |
| Bundled | [OscCore](https://github.com/vrchat/OscCore) in `Assets/OscCore-1.1.7` (MIT) |
| Headset | Meta Quest (Android build) or any OpenXR headset |
| Trackers | HaritoraX2 with a phone running Shiftall VR Manager, or any app that sends VRChat OSC trackers |

## Quick start (no hardware)

1. Open the project in Unity and load `Assets/FullBodyTracking/Demo/FBT_Demo.unity`.
2. Press **Play**.
3. After a moment the scene switches to *desktop simulation*. The avatar walks in place and a mirror shows it from the front.
4. Use the panel at the top left:
   - **Simulator** toggles the OSC stream on and off. Trackers time out and the avatar falls back to its animation pose.
   - **Motion** cycles Idle, WalkInPlace and Squat.
   - **IK** turns the IK on and off.
   - **Markers** shows or hides the coloured tracker spheres (blue is left, orange is right, green is centre).

The panel also shows the address the app listens on (for example `192.168.1.23:9000`). That is where your tracker sender must point.

## Using real trackers

![The avatar following real trackers](https://jon-gear.github.io/assets/images/internship-1/media/image48.gif)


1. **Build and install** the demo scene on the Quest (Android build target). With a headset connected the scene switches to XR mode on its own,
   the simulator goes quiet, and a status label appears in the world.
2. **Put the phone and the headset on the same Wi-Fi network.**
3. In Shiftall VR Manager (or your sender), set the OSC target to the **headset's IP address** and port **9000**.
   The IP and port are shown in the status label. Testing in the editor on the same PC? Use `127.0.0.1`.
4. Wear the trackers and calibrate in Shiftall VR Manager. The status label shows each tracker turning green when data arrives.


### Tracker numbering

The app expects these OSC addresses (the same ones VRChat uses). Each carries three floats: position in metres, or rotation as Euler angles in degrees, in Unity's coordinate system (+Y up).

| OSC address | Body part |
|---|---|
| `/tracking/trackers/head/position` and `/rotation` | Head (used to align tracker space, see below) |
| `/tracking/trackers/1/...` | Hip |
| `/tracking/trackers/2/...` | Left foot |
| `/tracking/trackers/3/...` | Right foot |
| `/tracking/trackers/4/...` | Left knee |
| `/tracking/trackers/5/...` | Right knee |
| `/tracking/trackers/6/...` | Chest |
| `/tracking/trackers/7/...` | Left elbow |
| `/tracking/trackers/8/...` | Right elbow |

Hands come from the XR controllers and the head from the headset. Missing trackers are simply skipped.

### Troubleshooting

- **`SocketException: Access denied` on the Quest.** The app has no network permission, which looks like OSC simply doing nothing. This project ships a custom `Assets/Plugins/Android/AndroidManifest.xml` with `INTERNET` and `ACCESS_NETWORK_STATE`. Keep it, and make sure your Meta XR / OpenXR build settings do not strip internet permission from the build.
- **Nothing arrives.** Check that phone and headset are on the same network and the IP matches the one shown. Restricted networks (for example Eduroam)
  can block device-to-device traffic, so use a home network or a phone hotspot.
- **Port already in use.** VRChat itself listens on 9000. If VRChat runs on the same PC, use another port (`Listen Port` on the controller, and the same port in the sender).
  The status panel shows a red message when the port could not be opened.
- **Editor on Windows.** Allow Unity through the Windows firewall for UDP.
- **Trackers show but the body looks wrong.** Check `Tracking Alignment` (below) and the head position is arriving.

### Debugging in layers

If something is not working, test the pipeline in small steps instead of going straight to a full character. This is how the project was built:

1. **Prove OSC works at all.** Send one float from a phone with [TouchOSC](https://hexler.net/touchosc) to the headset's IP and port, and inspect the traffic with [Protokol](https://hexler.net/protokol).

   ![TouchOSC to Unity test setup](https://jon-gear.github.io/assets/images/internship-1/diagrams/debugging-with-touch-osc.png)

2. **Make one value move something.** Use that float to move a cube.

   ![One OSC value moving a cube](https://jon-gear.github.io/assets/images/internship-1/media/image44.gif)

3. **Map every tracker address to a cube** and watch the "cube man" follow your body.

   ![Tracker data on debug cubes](https://jon-gear.github.io/assets/images/internship-1/media/image46.gif)

4. **Only then apply the targets to a humanoid with IK.** The **Markers** switch in this project shows the same tracker targets as coloured spheres.

   ![Applying tracker targets with IK](https://jon-gear.github.io/assets/images/internship-1/diagrams/adding-ik.png)


### How positions are aligned

`Tracking Alignment` on the controller has two modes:

- **HeadRelative** (default): each tracker is placed at `headset position + (tracker − OSC head position)`. This lets
  the sender's origin be anywhere. It requires the sender to also stream the head position.
- **Absolute**: OSC positions are used as Unity world positions directly.

## Project layout

```
Assets/
  FullBodyTracking/
    Scripts/        OscFullBodyIKController, OscTrackerSimulator, TrackerStatusHud, FbtDemoManager
    Prefabs/        FBT_Avatar_YBot  (avatar + tracker targets, already wired)
    Characters/     Y Bot, animator controller, materials
    Demo/           FBT_Demo.unity and its materials
    Experimental/   FinalIK_VRIK_Test.unity (optional, needs Final IK)
  OscCore-1.1.7/    OSC library (third party, MIT)
  Plugins/Android/  AndroidManifest.xml with the INTERNET permission (required for OSC on Quest)
  Samples, VRTemplateAssets, XR, XRI, Settings   Unity XR template content and settings
Docs/               README images
```

## The scripts

- **`OscFullBodyIKController`** sits on the avatar. It registers the OSC addresses, keeps the latest values per tracker
  (thread-safe, since OscCore delivers on its own thread), converts them to world-space targets, and applies
  them in `OnAnimatorIK`: hip via body position/rotation, feet via IK goals, knees and elbows via IK hints, chest and head via bone rotation, hands from the controllers.
  A tracker that stops sending for `Tracker Timeout` seconds is released back to the animation pose.
- **`OscTrackerSimulator`** animates a virtual performer sized to the avatar and sends it as real OSC over UDP (and moves the simulated headset and controllers).
- **`FbtDemoManager`** chooses XR or desktop at startup. **`TrackerStatusHud`** shows connection and tracker status.

## Using your own avatar

1. The model must be a **Humanoid** rig (Animator with an avatar), in a T-pose or A-pose.
2. Add `OscFullBodyIKController` to the avatar root and an Animator Controller with **IK Pass** enabled on the layer (`FBT_Base_Controller` has it).
3. Easiest is to duplicate `Prefabs/FBT_Avatar_YBot`, swap the model, and re-assign the controller's tracker targets if needed.
4. Keep the tracker target objects **outside** the avatar's hierarchy, because the avatar root moves.
5. If hands look twisted, adjust `Left/Right Hand Rotation Offset`. The defaults suit Mixamo-style rigs.

## Known limitations

- **A phone is required.** Running Shiftall VR Manager directly on the Quest was tried, but it was too slow, so the phone stays in the pipeline.
- **Setup effort.** Connecting trackers, running Shiftall VR Manager, configuring OSC, making the headset reachable and calibrating the body is fine for development, but a polished product would need to hide this behind a friendlier flow.
- **Network dependent.** Phone and headset must share a network that allows device-to-device traffic.
- Tracker rotations are used as sent: identity means *the avatar's rest pose facing +Z*. The app does no calibration of its own, so calibration is left to the sender (Shiftall VR Manager).
- HeadRelative alignment corrects **position** only. It does not rotate the sender's space to match the headset's yaw (VRChat does this from the head rotation). If your sender's forward direction differs from Unity's, trackers will look rotated.
- The avatar's head follows the spine from the hip tracker. It is not forced to the headset position, so the two can differ by a few centimetres.
- The final pose depends on avatar proportions and IK settings, so expect to tune per avatar.

## Optional: Final IK test scene

`Assets/FullBodyTracking/Experimental/FinalIK_VRIK_Test.unity` is an experiment with VRIK from Final IK, using hand-posed mock targets, explored to get more natural poses. The main demo uses only Unity's Animator IK.
Final IK is a commercial asset and is **not included** (and is git-ignored). Import it from the Asset Store into `Assets/Plugins/RootMotion` if you want this scene to work.

## Third-party content

- [OscCore](https://github.com/vrchat/OscCore): MIT, copyright Stella Cannefax. License in `Assets/OscCore-1.1.7/LICENSE`.
- Unity XR Interaction Toolkit samples and VR template assets: Unity package licenses.
- *Y Bot* is a Mixamo character, subject to Adobe's Mixamo terms.
- `Softball_player_rigged.fbx`: check its source license before redistributing.
- Images in this README are from [my portfolio](https://jon-gear.github.io/work/internship-1-short/).

## License

No license has been chosen for this project's own code yet. Add a `LICENSE` file before publishing.
