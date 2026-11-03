# Rotary Axis Control v0.1.24

## Display panel layout correction

- Recalculates the DRO height after the CamBam page completes initialization, preventing rotary-axis rows and lower readouts from being clipped or leaving unused gray space.
- Keeps the saved expanded height synchronized as the controller reports different rotary axes.

## Production panel: generate G-code for a drawing part

- Lists enabled CamBam parts that contain enabled machining operations.
- **Generate** builds toolpaths and G-code for the selected part using the active drawing's configured postprocessor.
- Selects the generated NC file in the existing Production file field so it can be sent or previewed.
- Refreshes the drawing-view preview when regenerating to the same output path.
- Sizes the part selector and Generate button together to keep the button visible at different panel widths.
- Runs one CamBam G-code generation pass for the selected part.
- Captures CamBam's selected output path before generation and reuses it, avoiding a second filename/overwrite prompt and correctly locating the generated file.
- Re-enables the part selector and Generate button after a simulation job completes or is stopped, so the next part can be loaded without restarting CamBam.
- Keeps the external NC path and Browse workflow available.

Close CamBam before installing. Run `Install-RotaryAxisControl.cmd` with Windows administrator privileges if CamBam is installed under Program Files, then restart CamBam. For the part-generation check, open a drawing with multiple machining parts, choose one in Production, click **Generate**, and verify the file and toolpath preview correspond to that part. The dropdown includes enabled parts and enabled operations only. Use the DRO checks in `TESTING.md` to verify the display with each simulator axis profile.

The plugin is an independent derivative of GRBLMachinePlugin by KDesign Electronics / Karst Drenth. See `THIRD-PARTY-NOTICES.txt` for attribution and dependency notices.
