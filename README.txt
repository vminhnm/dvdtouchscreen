dvdtouchscreen - mobile-style touch controls for Dave the Diver on a Windows touchscreen
=====================================================================================
How it works: a transparent overlay draws the mobile layout on top of the game and
feeds a virtual Xbox 360 controller (ViGEmBus). Dave the Diver sees a normal gamepad.
Touches outside the buttons pass straight through to the game.

Requirements
 - ViGEmBus driver installed (the same one TBVGPE uses): github.com/nefarius/ViGEmBus/releases
 - Windows 10/11 touchscreen, .NET Framework 4.8 (built in)
 - Game in Borderless/Windowed fullscreen (exclusive fullscreen hides overlays)

Use
 1. Run dvdtouchscreen.exe (keep Nefarius.ViGEm.Client.dll next to it).
 2. Start the game; in-game controls should show as Xbox pad.
 3. Left stick = move Dave.
    Big orange button: touch and hold = A is held, drag = moves the left stick,
    release = taps RT, then lets go of A. Release over the CANCEL circle to let go
    of A without tapping RT.
    The small stick under the shoulder buttons is the right stick (look/aim).
    Every button is fixed and sends the Xbox button it shows: A B X Y, LB RB, LT RT,
    View (Back) and Menu (Start). The D-pad is one icon: touch its up/down/left/right
    arm (touch near a corner for two directions).
 4. The ☰ button (top centre) opens settings: size, opacity, fixed/floating stick,
    D-pad, and monitor.
 5. "Edit layout" lets you drag every button; press Save.

Settings: %APPDATA%\dvdtouchscreen\config.ini
Rebuild: run build.bat. Mouse also works for testing (acts as one finger).

Edit layout
 Open the menu button, tick "Edit layout". Drag any button (including the CANCEL
 circle) to move it. Tap a button, then drag the yellow handle at its lower-right
 to resize it. Open the menu again and press Save.
