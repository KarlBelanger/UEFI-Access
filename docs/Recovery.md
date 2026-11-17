# Recovery: what to do if the machine does not start normally

This page is for anyone whose Dell OptiPlex 7020 does not start Windows normally after a boot-order or
one-time-boot change made with UEFI-Access. The 2014 OptiPlex 7020 has no firmware recovery menu, so
keep this page (and the printout of Dell KB 000124377, "How to Reset Your Dell BIOS or CMOS & Clear
NVRAM") where you can reach it without the machine.

Go through the parts in order and stop at the first one that fits:

| What you see | Go to |
|---|---|
| A screen titled "BitLocker recovery" that asks for a key | Part A |
| Windows does not start, or something other than the usual Windows starts | Part B |
| No Dell logo at all: the machine does not start its own checks (no POST) | Part C |

Part D lists what you must never do. If in doubt: stop, leave the machine as it is.

## What UEFI-Access changes

UEFI-Access changes only two things, and only after you confirm each change in a dialog:

- the order in which the machine tries its start entries (the boot order, BootOrder);
- a "start this entry once" choice for the next restart (the one-time boot, BootNext).

It never changes a BIOS setting, Secure Boot, a password or a BitLocker key. When you tick the box for
it, it suspends BitLocker for one restart. Before every change it saves a copy of the old values (a
snapshot), so the old boot order can be put back.

Words used here: F12 menu is the one-time boot menu (press F12 at the Dell logo); F2 Setup is
the machine's own settings screens (press F2 at the Dell logo); Windows Boot Manager is the start
entry that starts Windows; POST is the machine's own checks before the Dell logo; recovery
password is the 48-digit number that unlocks drive C: when BitLocker's check fails.

## Part A: a BitLocker recovery screen

You see a screen titled "BitLocker recovery" that asks for a key and shows a "Recovery key ID" (the ID
is a label, not the secret).

1. Do not switch the machine off.
2. Find the 48-digit recovery password you saved when BitLocker was turned on (in your Microsoft
   account, on a printout, or on a USB key), and check that its key ID starts with the same characters
   as the Recovery key ID on the screen.
3. Type the 48 digits on the keyboard (the screen adds the dashes itself; on this screen F1 to F10 also
   type 1 to 0) and press Enter.
4. Wait for Windows to start.

If the key is refused, re-type each group of six digits. Without this password nobody can unlock drive
C:. Never read the recovery password aloud, type it anywhere but this screen, copy it or photograph
it. UEFI-Access never reads, shows or keeps it.

## Part B: Windows does not start, or the wrong entry starts

You see an error such as "No bootable devices found", a Windows repair screen, a machine that keeps
restarting, or anything other than the usual Windows sign-in. (If the screen says "BitLocker recovery",
do Part A first.)

### B1. Start Windows once from the F12 menu

1. Hold the power button until the power light goes off (about 5 seconds), wait 10 seconds, then press
   the power button.
2. When the Dell logo appears, press F12 about once a second until the menu opens.
3. Under "UEFI BOOT", select "Windows Boot Manager" and press Enter.
4. If a BitLocker recovery screen appears, do Part A.
5. When Windows is back, change nothing in F2 Setup yet; undo the change from inside Windows (B2).

### B2. Undo the change from inside Windows

Once Windows is running, read the current state before changing anything: `uefiaccess boot` shows its
BootOrder and BootNext lines. Then take the first case that fits:

1. BootOrder already starts with Windows Boot Manager and BootNext is not set. Nothing to undo.
2. BootNext is still set (a one-time boot the firmware did not use). Remove it: in the application,
   the BootNext row, `Alt+M` (Clear one-time boot), then `Alt+F` (Confirm change); or on the command
   line, `uefiaccess write bootnext --clear` as a dry run, then the same line with `--confirm`.
3. A UEFI-Access reorder put the wrong entry first. Put the order back from the snapshot of that
   change: the snapshot folder whose name ends in `-reorder` (never a `-restore` or `-bootnext` one).
   Each change names its snapshot when it is made (a folder named with the change's date and time,
   ending `-reorder`).
   On the command line, first the dry run
   `uefiaccess write restore <that folder> --bitlocker suspend`, then the same line with `--confirm`
   (use `--bitlocker continue` instead if the tool says the suspend is not offered). In the
   application, Restore previous value (`Alt+P`) may be used instead only when the dialog's
   `What will change` names that same folder and its After box lists Windows Boot Manager first, because
   `Alt+P` always takes the newest snapshot; otherwise press Cancel (`Escape`). Then restart once and
   check that Windows starts normally.

### B3. If that does not work: put Windows Boot Manager first in F2 Setup

Do this only if the F12 menu did not start Windows.

1. If Windows can still start: open Control Panel, System and Security, BitLocker Drive Encryption, and
   next to drive C: choose "Suspend protection", then "Yes" (so the change below does not trigger a
   recovery screen). If Windows cannot start, skip this step; the next start will then ask for the
   recovery password (Part A).
2. Restart, and press F2 about once a second at the Dell logo until Setup opens.
3. Open General, then Boot Sequence; select "Windows Boot Manager" and move it to the top of the list
   with the up-arrow button. Change nothing else.
4. Apply, OK, Exit. The machine restarts.
5. If a BitLocker recovery screen appears, do Part A.

## Part C: the machine does not POST

You press the power button and there is no Dell logo, no picture, beeps, or a blinking or amber power
light.

### C1. Check the simple things first

1. Check the monitor is on and its cable is in the computer's own DisplayPort or VGA socket (the back
   panel of the main board, not a separate card).
2. Hold the power button until the power light goes off, unplug the power cable for 30 seconds, plug it
   back in and press the power button.

### C2. The RTCRST jumper reset (Dell KB 000124377)

Use this only when the machine does not POST. What it does:

- It resets every BIOS setting to Dell's defaults and clears the BIOS passwords.
- It does not repair a failed BIOS update, and this tool never updates the BIOS. Never flash the
  BIOS as a repair: on this model a failed update cannot be repaired with the jumper.
- Whether it also clears the start entries (the boot list) is not known.
- After it, BitLocker will ask for the recovery password (the defaults hide the TPM, so BitLocker
  cannot unlock drive C: by itself). Have the recovery password ready (Part A).

With the machine off and unplugged, and after touching bare metal of the case: move the jumper cap from
the PSWD pins to the RTCRST pins, plug the power cable in (do not press the power button), wait 10
seconds, unplug again, move the cap back to the PSWD pins, close the cover, plug in and start. The
owner's manual and Dell KB 000124377 show where the PSWD and RTCRST jumpers are. Never start the
machine with the cap off the PSWD pins except during this reset (that clears the BIOS passwords).

### C3. Put back the five settings in F2 Setup

The reset restored Dell's defaults. Set these five again, and nothing else (Apply after each):

1. General, Boot Sequence: Boot List Option = UEFI.
2. General, Advanced Boot Options: untick Enable Legacy Option ROMs.
3. Secure Boot, Secure Boot Enable = Enabled.
4. Security: tick TPM Security (on), Apply, then TPM Activation = Activate (never "Clear"; if
   Activate cannot be chosen yet, Apply, Exit, press F2 again at the next logo and set it then).
5. System Configuration, SATA Operation = AHCI.

Then check that "Windows Boot Manager" is in General, Boot Sequence. If the list is empty, add it with
"Add Boot Option" (name Windows Boot Manager, file `\EFI\Microsoft\Boot\bootmgfw.efi` on the first
partition). Exit; the machine restarts, and a BitLocker recovery screen appears (Part A).

## Part D: never do these

- Never update ("flash") the BIOS as a repair, and never choose "BIOS Flash Update" in the F12 menu.
- Never choose "Change Boot Mode Settings" in the F12 menu.
- Never change SATA Operation away from AHCI (Windows then stops with INACCESSIBLE_BOOT_DEVICE); the
  only time you touch it is C3 step 5, to put AHCI back.
- Never clear, delete or change the Secure Boot keys, and never turn Secure Boot off.
- Never choose "Clear" or "Deactivate" under TPM.
- Never switch off or restart while UefiAccess shows "Writing the change, please wait. Do not restart
  the machine." If Windows says UefiAccess is keeping it from restarting, choose Cancel.
- In F2 Setup, never change anything this page does not name.
