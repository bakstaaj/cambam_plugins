# Rotary Axis Control test procedure (v0.1.18)

## Package installation

1. Close CamBam.
2. Copy `RotaryAxisCamBamPlugin.dll` and, if it is not already present, `CamBam.CAD.Solids.dll` from the package into CamBam's existing plugin directory (the directory that contains its other plugin DLLs). The installer leaves an existing solids DLL in place; back it up before manually replacing it.
3. Start CamBam and open **Plugins → Rotary Axis Control**.
4. In the **Connection** tab, leave the physical connection closed, select **XYZ + A (4-axis)**, and click **START SIM**. Confirm the version is marked `SIM`, then test a small positive and negative jog and index to a nonzero target angle.
5. Click **Set work zero**. Check the coordinate mode shown beside the top display: MPos remains the machine position. Click the mode label beside the display to switch from **MPos** to **WPos** and confirm the selected rotary axis reads zero.
6. Stop the simulator, choose **XYZ + A/B (5-axis)**, and start it again. Confirm the upper display grows to show A and B, with both values in degrees. Repeat with **XYZ + A/B/C (6-axis)** and confirm the third row appears and the display grows again.
7. Select a valid `.nc` file. The right drawing view should show rapid moves in gray and cutting moves in blue. The file is previewed as an overlay and does not add or alter geometry in the CamBam drawing. Large files are sampled to keep the preview responsive.
8. Confirm the DRO uses the dark slate background and high contrast values. Idle, Run, and Hold should use the same mint green as the coordinate values; Alarm remains red. In the Connection panel, confirm the simulator version text no longer overlaps the **STOP SIM** button and returns when the simulator disconnects. Confirm **Play** becomes available while the simulator is active and that the **Sim speed** buttons appear beside the transport controls; the Production panel should grow enough to show the entire speed row. Run the same job at 1x, 2x, 5x, and 10x; the marker should move through intermediate positions faster at higher multipliers, and the selected speed should remain highlighted. Short moves should also respond to the selected multiplier. Try changing speed during a move. The speed buttons should disappear and the panel return to its normal height after disconnecting the simulator; they should remain hidden with a live controller. During playback, confirm the view stays framed on the preview and the tool marker advances. Exercise Pause, Resume, and Stop. The displayed coordinates should follow the file's axis moves; no command is sent to a serial port and M6 tool changes are skipped. The simulator estimates duration from programmed feed or a simulated rapid rate, so observe that Z retracts finish before subsequent XY traverses. Feed units default to mm/min in simulator mode, and spindle commands such as M3 S12000 should show the programmed RPM until M5.
9. Select another file, then clear the file path. Confirm the old preview disappears when the selection is cleared.
10. Click **STOP SIM** when no job is running.

The initial preview draws linear G0/G1 motion in XY and Z, with G20/G21 and G90/G91 unit and distance modes. Arc moves and rotary-only motion are not yet projected into the 2D drawing preview; the rotary DRO still reports rotary position.

To remove the test build, close CamBam and delete `RotaryAxisCamBamPlugin.dll`. Keep `CamBam.CAD.Solids.dll` if the KDesign 3D Solids plugin or another CamBam plugin uses it. The plugin keeps its settings in `RotaryAxisControl.config` and its postprocessor in `post/RotaryAxisControl.cbpp` under CamBam's system configuration folder.

## Firmware and machine preflight

The simulator is for interface, sender, and command-flow checks only. It does not prove that firmware accepts the axis, that the rotary motor turns in the expected direction, or that real motion is safe. Use the following checks only when a compatible controller is available.

- Use GRBL-derived firmware that accepts the configured A, B, or C axis and includes that axis in its live position report. Stock three-axis GRBL will leave the rotary controls disabled.
- Confirm the rotary axis is configured in the controller and that the reported rotary units are degrees.
- Remove the tool or ensure it is well clear of the work and rotary fixture. Keep the spindle off and supervise the machine throughout.
- Start with a low rotary jog and index feed. The UI defaults both feeds to 30 degrees per minute and the jog increment to 1 degree.

## First motion checks

1. Connect to the controller and select the axis letter configured in its firmware.
2. Confirm the live rotary position appears and the warning changes to the tool-clear reminder. If the controller does not report the axis, do not attempt to use the controls.
3. Jog by a small positive increment, then a small negative increment. Confirm the displayed angle follows the controller and the direction is understood.
4. With the axis stationary, click **Set work zero** and confirm the controller reports the new work coordinate.
5. Enter a small target angle (for example, 10 degrees) and click **Index**. Confirm motion stops at the commanded work angle.
6. Confirm the controls disable during a job and whenever the controller is not idle; verify they re-enable after the job ends and the controller returns to idle.

Do not use this first build for cutting, unattended movement, or a production job. This procedure checks manual rotary controls only; wrapped CAM, simultaneous 4-axis moves, retract automation, and rotary homing are not implemented.
