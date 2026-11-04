# name=FLPTools Companion
# url=https://github.com/odinong/FLPTools
# FLPTools mod pack
"""
Runs inside FL Studio as a MIDI controller script and quietly helps while you work:
  - colours channels, mixer tracks and playlist tracks from words in their names (kick, 808, vox...)
  - names and colours an empty mixer track after the channel routed to it
  - saves the project every few minutes when there are unsaved changes
  - shows how long you have been working in FL's hint bar
Settings live in flptools_companion.json next to this file; FLPTools writes it and changes apply live.
MIDI coming in on the port this script is attached to is passed through to FL untouched.
"""
import os
import json
import time

import channels
import general
import midi
import mixer
import playlist
import transport
import ui

HERE = os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else os.path.join(
    os.path.expanduser('~'), 'Documents', 'Image-Line', 'FL Studio', 'Settings', 'Hardware', 'FLPTools Companion')
CONFIG = os.path.join(HERE, 'flptools_companion.json')
LOG = os.path.join(HERE, 'flptools_companion.log')

DEFAULTS = {
    'autoColor': True,
    'colorChannels': True,
    'colorMixer': True,
    'colorPlaylist': True,
    'mixerFollowsChannel': True,
    'autosaveMinutes': 0,
    'sessionHintMinutes': 30,
    'rules': [
        {'words': ['kick', 'kik', 'bd'], 'color': '#e5533d'},
        {'words': ['snare', 'clap', 'rim', 'snr'], 'color': '#f0a030'},
        {'words': ['hat', 'hh', 'hihat', 'cymbal', 'ride', 'crash', 'open'], 'color': '#e8d44d'},
        {'words': ['perc', 'shaker', 'tom', 'conga', 'bongo', 'tamb'], 'color': '#8bc34a'},
        {'words': ['808', 'bass', 'sub'], 'color': '#9b59d0'},
        {'words': ['vox', 'vocal', 'voc', 'adlib', 'voice', 'chop'], 'color': '#e85d9e'},
        {'words': ['pad', 'chord', 'keys', 'piano', 'synth', 'lead', 'pluck', 'bell', 'melody', 'arp', 'guitar'], 'color': '#3d8fe0'},
        {'words': ['fx', 'riser', 'impact', 'sweep', 'noise', 'reverse', 'transition', 'drop'], 'color': '#3cc7c7'},
        {'words': ['bus', 'send', 'reverb', 'delay', 'group'], 'color': '#7f8c8d'},
    ],
}

cfg = dict(DEFAULTS)
cfg_mtime = 0.0
seen = {'ch': {}, 'mx': {}, 'pl': {}, 'route': {}}
session_start = time.time()
last_hint = time.time()
last_save = time.time()
last_tick = 0.0
last_cfg_check = 0.0
ready = False


def log(*parts):
    try:
        with open(LOG, 'a', encoding='utf-8') as f:
            f.write(time.strftime('%Y-%m-%d %H:%M:%S ') + ' '.join(str(p) for p in parts) + '\n')
    except Exception:
        pass


def load_config():
    global cfg, cfg_mtime
    try:
        m = os.path.getmtime(CONFIG)
        if m == cfg_mtime:
            return
        with open(CONFIG, encoding='utf-8') as f:
            data = json.load(f)
        merged = dict(DEFAULTS)
        merged.update({k: v for k, v in data.items() if k in DEFAULTS})
        cfg, cfg_mtime = merged, m
        log('config loaded')
    except FileNotFoundError:
        cfg_mtime = 0.0
    except Exception as e:
        log('config error', repr(e))


def hex_to_color(h):
    h = h.lstrip('#')
    return int(h[:6], 16)


def color_for(name):
    words = ''.join(c if c.isalnum() else ' ' for c in name.lower()).split()
    joined = ' '.join(words)
    for rule in cfg['rules']:
        for w in rule.get('words', []):
            w = w.lower().strip()
            if not w:
                continue
            if w in words or (len(w) > 3 and w in joined):
                return hex_to_color(rule['color'])
    return None


def snapshot():
    """Remember current names so only later renames trigger colouring."""
    try:
        seen['ch'] = {i: channels.getChannelName(i) for i in range(channels.channelCount())}
        seen['mx'] = {i: mixer.getTrackName(i) for i in range(1, mixer.trackCount() - 1)}
        seen['pl'] = {i: playlist.getTrackName(i) for i in range(1, playlist.trackCount() + 1)}
        seen['route'] = {i: channels.getTargetFxTrack(i) for i in range(channels.channelCount())}
    except Exception as e:
        log('snapshot error', repr(e))


def is_default_mixer_name(i, name):
    return not name or name.strip().lower() in ('insert %d' % i, 'insert%d' % i, '')


def auto_color():
    if not cfg['autoColor']:
        return
    if cfg['colorChannels']:
        for i in range(channels.channelCount()):
            name = channels.getChannelName(i)
            if seen['ch'].get(i) != name:
                seen['ch'][i] = name
                c = color_for(name)
                if c is not None:
                    channels.setChannelColor(i, c)
    if cfg['colorMixer']:
        for i in range(1, mixer.trackCount() - 1):
            name = mixer.getTrackName(i)
            if seen['mx'].get(i) != name:
                seen['mx'][i] = name
                c = color_for(name)
                if c is not None and not is_default_mixer_name(i, name):
                    mixer.setTrackColor(i, c)
    if cfg['colorPlaylist']:
        for i in range(1, playlist.trackCount() + 1):
            name = playlist.getTrackName(i)
            if seen['pl'].get(i) != name:
                seen['pl'][i] = name
                c = color_for(name)
                if c is not None:
                    playlist.setTrackColor(i, c)


def mixer_follows_channel():
    if not cfg['mixerFollowsChannel']:
        return
    for i in range(channels.channelCount()):
        target = channels.getTargetFxTrack(i)
        name = channels.getChannelName(i)
        key = (target, name)
        if seen['route'].get(i) == key:
            continue
        seen['route'][i] = key
        if target <= 0 or target >= mixer.trackCount() - 1:
            continue
        if is_default_mixer_name(target, mixer.getTrackName(target)):
            mixer.setTrackName(target, name)
            mixer.setTrackColor(target, channels.getChannelColor(i) & 0xFFFFFF)
            seen['mx'][target] = name


def autosave():
    global last_save
    minutes = cfg['autosaveMinutes']
    if not minutes or time.time() - last_save < minutes * 60:
        return
    last_save = time.time()
    if general.getChangedFlag() > 0 and not transport.isRecording():
        transport.globalTransport(midi.FPT_Save, 1)
        ui.setHintMsg('FLPTools: project autosaved')
        log('autosaved')


def session_hint():
    global last_hint
    minutes = cfg['sessionHintMinutes']
    if not minutes or time.time() - last_hint < minutes * 60:
        return
    last_hint = time.time()
    spent = int((time.time() - session_start) / 60)
    ui.setHintMsg('FLPTools: working for %d h %02d min' % (spent // 60, spent % 60) if spent >= 60 else 'FLPTools: working for %d min' % spent)


def OnInit():
    global ready
    load_config()
    snapshot()
    ready = True
    log('started in FL', ui.getVersion() if hasattr(ui, 'getVersion') else '')
    ui.setHintMsg('FLPTools companion is on')


def OnProjectLoad(status):
    # status 100 = loaded; re-read names so the opened project is not recoloured
    if status == 100:
        snapshot()


def OnIdle():
    global last_tick, last_cfg_check
    if not ready:
        return
    now = time.time()
    if now - last_cfg_check > 5:
        last_cfg_check = now
        load_config()
    if now - last_tick < 1.5:
        return
    last_tick = now
    for step in (mixer_follows_channel, auto_color, autosave, session_hint):
        try:
            step()
        except Exception as e:
            log(step.__name__, 'error', repr(e))


def OnMidiMsg(event):
    event.handled = False


def OnDeInit():
    log('stopped')
