# Rotary Axis Control release v0.1.19

Highlights:
- Adds serial or raw TCP/IP Ethernet connection options with host and port fields.
- Separates the connection controls from the simulator and expands the panel to prevent overlap.
- Includes the corrected Release plugin DLL with embedded resources.

Ethernet currently targets controllers or adapters that expose a raw GRBL-compatible TCP socket. It has not been validated against a live Ethernet controller.

The plugin is an independent derivative of GRBLMachinePlugin by KDesign Electronics / Karst Drenth. See README.md and THIRD-PARTY-NOTICES.txt for attribution.
