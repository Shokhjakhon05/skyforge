#!/bin/bash
# Drives the installed game on the emulator: see .github/workflows/smoke-android.yml.
# Coordinates are for a 2160x1080 landscape screen (the pixel_3 profile), where the game's
# canvas is 1080 units high and one unit is one pixel.
PKG=com.zerohour.sniper
OUT=smoke
mkdir -p $OUT
shot() { adb exec-out screencap -p > "$OUT/$1.png" 2>/dev/null; echo "shot $1: $(stat -c %s "$OUT/$1.png" 2>/dev/null) bytes"; }
alive() { adb shell pidof $PKG | tr -d '\r'; }

adb logcat -c
# Android's own "viewing full screen" card would cover the game on a first start.
adb shell settings put secure immersive_mode_confirmations confirmed
adb shell settings put global policy_control immersive.full=*
APK=$(ls *.apk | head -1)
echo "installing $APK"
adb install -r "$APK" || { echo "INSTALL FAILED" | tee $OUT/result.txt; exit 0; }
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 12
shot 01_starting
# In case the card came up anyway: its "Got it".
adb shell input tap 1930 553
sleep 30
shot 02_after_splash
echo "pid after start: $(alive)"
adb shell wm size

# A new player is paid for turning up before anything else: collect it.
adb shell input tap 1080 682
sleep 4
shot 02b_menu
# The first contract's card, then "take the shot".
adb shell input tap 214 705
sleep 8
shot 03_briefing
adb shell input tap 804 832
sleep 12
shot 04_in_position
# Zoom in twice, drag the cross, hold breath, fire.
adb shell input tap 2030 390
sleep 1
adb shell input tap 2030 390
sleep 2
adb shell input swipe 700 500 760 470 400
sleep 2
shot 05_zoomed
adb shell input tap 220 870
sleep 1
adb shell input tap 1940 835
sleep 3
shot 06_after_shot
sleep 10
shot 07_later
echo "pid at the end: $(alive)"

adb logcat -d > $OUT/logcat_all.txt 2>/dev/null
grep -E "Unity|\[Sniper\]|\[Skyforge\]|FATAL|AndroidRuntime|CRASH|SIGSEGV|signal 11" $OUT/logcat_all.txt | tail -400 > $OUT/logcat_game.txt
if [ -n "$(alive)" ]; then echo "ALIVE AT THE END" | tee $OUT/result.txt; else echo "NOT RUNNING AT THE END" | tee $OUT/result.txt; fi
grep -c "FATAL EXCEPTION" $OUT/logcat_all.txt | sed 's/^/fatal exceptions: /' | tee -a $OUT/result.txt
gzip -f $OUT/logcat_all.txt
exit 0
