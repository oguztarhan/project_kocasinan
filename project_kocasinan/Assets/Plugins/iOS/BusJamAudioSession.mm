// BusJamAudioSession.mm
// Makes in-game audio (music + SFX) play on iOS even when the device's hardware SILENT/MUTE switch is ON —
// matching Android, where media audio always plays. Unity's default iOS audio session category is muted by the
// silent switch, so testers with the ringer switched off heard NO music. Setting the category to .Playback makes
// audio ignore the silent switch. Called from MusicManager (startup + on app resume, since interruptions such as a
// phone call or Siri can reset the session).
#import <AVFoundation/AVFoundation.h>

extern "C" void _BusJamSetAudioSessionPlayback(void)
{
    // These AVAudioSession calls report failures via the NSError out-param (not Obj-C
    // exceptions), so no @try/@catch is needed — and Unity's iOS build compiles plugins
    // with Objective-C exceptions disabled, which made the previous @try fail to build.
    AVAudioSession *session = [AVAudioSession sharedInstance];
    NSError *err = nil;
    // .Playback = plays regardless of the silent switch. (No MixWithOthers: the game owns audio output.)
    [session setCategory:AVAudioSessionCategoryPlayback error:&err];
    [session setActive:YES error:&err];
}
