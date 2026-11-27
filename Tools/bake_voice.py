"""해설 · NPC 대사를 Gemini 음성으로 미리 녹음한다 (STILL — VoiceBank).

    python Tools/bake_voice.py            # 아직 녹음 없는 대사를 전부 (한도에 걸리면 멈춘다 — 다음에 다시 실행하면 이어서)
    python Tools/bake_voice.py --limit 10 # 이번에는 10줄만

순서: Unity 메뉴 DomeAquarium/음성/1. 대사 목록 만들기 → 이 스크립트 → DomeAquarium/음성/2. 녹음 등록.
  - 대사 목록: Assets/DomeAquarium/Audio/Voice/lines.json (열쇠 · 목소리 · 문장). 녹음은 같은 폴더의 {열쇠}.wav.
  - 키: 프로젝트 루트 .env 의 GEMINI_API_KEY. 키는 화면에 찍지 않는다.
  - 한도: 분당 한도(429)에 걸리면 1분 쉬고 다시, 하루 한도에 걸리면 멈춘다. 이미 녹음한 줄은 건너뛰니 다음 날 그대로 다시 실행하면 된다.
  - 앱이 하던 그대로 문장만 보내고(말투 지시 없음), 받은 16-bit 모노 PCM 을 wav 로 감싼다.
"""
import argparse
import base64
import json
import os
import re
import struct
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VOICE_DIR = os.path.join(ROOT, 'Assets', 'DomeAquarium', 'Audio', 'Voice')
ENDPOINT = 'https://generativelanguage.googleapis.com/v1beta/models/{}:generateContent'


def env_key():
    path = os.path.join(ROOT, '.env')
    if not os.path.exists(path):
        return None
    for raw in open(path, encoding='utf-8'):
        line = raw.strip()
        if line.startswith('#') or '=' not in line:
            continue
        name, value = line.split('=', 1)
        if name.strip().upper() == 'GEMINI_API_KEY':
            value = value.strip().strip('"\'')
            return value or None
    return None


def wav_bytes(pcm, rate):
    return (b'RIFF' + struct.pack('<I', 36 + len(pcm)) + b'WAVE' +
            b'fmt ' + struct.pack('<IHHIIHH', 16, 1, 1, rate, rate * 2, 2, 16) +
            b'data' + struct.pack('<I', len(pcm)) + pcm)


class Stop(Exception):
    pass


def speak(key, model, voice, text):
    body = {'contents': [{'parts': [{'text': text}]}],
            'generationConfig': {'responseModalities': ['AUDIO'],
                                 'speechConfig': {'voiceConfig': {'prebuiltVoiceConfig': {'voiceName': voice}}}}}
    req = urllib.request.Request(ENDPOINT.format(model), data=json.dumps(body).encode('utf-8'), method='POST',
                                 headers={'Content-Type': 'application/json', 'x-goog-api-key': key})
    with urllib.request.urlopen(req, timeout=120) as r:
        data = json.loads(r.read())
    for cand in data.get('candidates', []):
        for part in cand.get('content', {}).get('parts', []):
            inline = part.get('inlineData') or part.get('inline_data')
            if inline and inline.get('data'):
                mime = inline.get('mimeType', '')
                m = re.search(r'rate=(\d+)', mime)
                return base64.b64decode(inline['data']), int(m.group(1)) if m else 24000
    raise RuntimeError('응답에 오디오가 없다 (모델 이름이 TTS 모델인지 확인)')


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')   # Windows 콘솔(cp949)에서 한글이 깨지지 않게
    ap = argparse.ArgumentParser()
    ap.add_argument('--limit', type=int, default=0, help='이번에 녹음할 최대 줄 수 (0 = 전부)')
    ap.add_argument('--interval', type=float, default=7.0, help='요청 사이 쉬는 초')
    ap.add_argument('--model', default='gemini-2.5-flash-preview-tts')
    args = ap.parse_args()

    key = env_key()
    if not key:
        sys.exit('.env 에 GEMINI_API_KEY 가 없다.')
    lines = json.load(open(os.path.join(VOICE_DIR, 'lines.json'), encoding='utf-8'))['lines']
    todo = [l for l in lines if not os.path.exists(os.path.join(VOICE_DIR, l['key'] + '.wav'))]
    # 하루 한도가 십여 줄이라 중요한 순서로 — 본편(로비 · 층 · NPC) → 밤 이야기 → 그 밖 → 생물 설명(116줄)은 맨 뒤
    order = ['LobbySequence', 'LobbyTutorial', 'DiveMission', 'DiveDirector', 'ObservationTerms', 'FlashOrWatch',
             'EntangledTurtle', 'WhaleFall', 'HydrophoneCalls', 'NpcDialogue', 'BeachCleanup', 'TidePoolTouch',
             'FlukePhotoId', 'BinocularSubject', 'LighthouseSignalStory', 'TurtleNightStory', 'CaptainNightStory',
             'NextSignalStory', 'BathysphereStory']

    def rank(l):
        src = l.get('from', '').split(' ')[0]
        if src == 'FishData':
            return len(order) + 1
        return order.index(src) if src in order else len(order)
    todo.sort(key=rank)                                               # 안정 정렬 — 같은 곳의 대사는 목록 차례 그대로
    print(f'대사 {len(lines)}줄 · 녹음 있음 {len(lines) - len(todo)} · 남음 {len(todo)}')
    if args.limit:
        todo = todo[:args.limit]

    done = 0
    interval = args.interval
    try:
        for i, l in enumerate(todo):
            prefix = ''
            for attempt in range(3):
                try:
                    pcm, rate = speak(key, args.model, l['voice'], prefix + l['text'])
                    break
                except urllib.error.HTTPError as e:
                    body = e.read().decode('utf-8', 'replace')
                    try:
                        err = json.loads(body)['error']
                    except Exception:
                        err = {'message': body[:300]}
                    msg = err.get('message', '')
                    if e.code == 429:
                        if 'PerDay' in body or 'per day' in msg.lower():
                            raise Stop('하루 한도에 걸렸다 — 내일 다시 실행하면 이어서 녹음한다.')
                        interval = max(interval, 21.0)          # 분당 한도 — 1분 쉬고, 이후로는 천천히
                        print('  분당 한도 — 60초 쉰다')
                        time.sleep(60)
                        continue
                    if e.code == 400 and 'only be used for TTS' in msg:
                        # 묻는 말 · 부탁하는 말("…알려 주세요")을 지시로 알아듣고 글로 답하려 한 것 — 읽으라고 못 박아 다시 보낸다
                        prefix = 'Read the following Korean line aloud exactly as written, without answering it: '
                        print(f'  읽기 지시를 붙여 다시({attempt + 1}/3): {l["text"][:30]}')
                        time.sleep(3)
                        continue
                    raise Stop(f'HTTP {e.code} {err.get("status", "")}: {msg[:300]}')
                except (urllib.error.URLError, TimeoutError, RuntimeError) as e:
                    print(f'  실패({attempt + 1}/3): {e}')
                    time.sleep(10)
            else:
                print(f'  건너뜀: {l["text"][:40]}')
                continue
            with open(os.path.join(VOICE_DIR, l['key'] + '.wav'), 'wb') as f:
                f.write(wav_bytes(pcm, rate))
            done += 1
            print(f'[{i + 1}/{len(todo)}] {l["voice"]} · {len(pcm) / rate / 2:.1f}초 · {l["text"][:40]}')
            time.sleep(interval)
    except Stop as s:
        print(s)
    except KeyboardInterrupt:
        print('멈춤')
    print(f'이번에 {done}줄 녹음. Unity 에서 DomeAquarium/음성/2. 녹음 등록을 누르면 앱에 들어간다.')


if __name__ == '__main__':
    main()
