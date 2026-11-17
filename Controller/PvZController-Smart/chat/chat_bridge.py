"""Reads a TikTok live chat and forwards it to the PvZ controller (127.0.0.1:8082).

Started automatically by PvZController.exe. Needs: pip install TikTokLive
The streamer name is in username.txt. Runs forever: if the stream is offline it retries.
"""
import json
import os
import sys
import threading
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
URL = "http://127.0.0.1:8082/trigger_effect"


def username():
    try:
        with open(os.path.join(HERE, "username.txt"), encoding="utf-8") as f:
            name = f.read().strip().lstrip("@")
            if name:
                return name
    except OSError:
        pass
    return ""


def post(effect, **fields):
    body = json.dumps({"effect_id": effect, **fields}).encode("utf-8")

    def send():
        try:
            req = urllib.request.Request(URL, data=body, headers={"Content-Type": "application/json"})
            urllib.request.urlopen(req, timeout=3).read()
        except Exception:
            pass

    threading.Thread(target=send, daemon=True).start()


def who(event):
    u = getattr(event, "user", None)
    if u is None:
        return "", ""
    return (getattr(u, "nickname", "") or ""), (getattr(u, "unique_id", "") or "")


def main():
    name = username()
    if not name:
        print("CHAT no username in chat/username.txt", flush=True)
        return
    try:
        from TikTokLive import TikTokLiveClient
        from TikTokLive.events import (CommentEvent, ConnectEvent, DisconnectEvent,
                                       FollowEvent, GiftEvent, JoinEvent)
    except Exception as ex:
        print(f"CHAT TikTokLive is not installed ({ex}). Run: pip install TikTokLive", flush=True)
        return

    while True:
        client = TikTokLiveClient(unique_id="@" + name)

        @client.on(ConnectEvent)
        async def on_connect(event):
            print(f"CHAT connected to @{name}", flush=True)

        @client.on(DisconnectEvent)
        async def on_disconnect(event):
            print("CHAT disconnected", flush=True)

        @client.on(CommentEvent)
        async def on_comment(event):
            nick, uid = who(event)
            post("ChatComment", user=nick or uid, handle=uid, text=event.comment or "")

        @client.on(JoinEvent)
        async def on_join(event):
            nick, uid = who(event)
            post("ChatJoin", user=nick or uid, handle=uid)

        @client.on(FollowEvent)
        async def on_follow(event):
            nick, uid = who(event)
            post("ChatFollow", user=nick or uid, handle=uid)

        @client.on(GiftEvent)
        async def on_gift(event):
            # Streakable gifts fire repeatedly while the streak is running; report once when it ends.
            gift = getattr(event, "gift", None)
            if gift is not None and getattr(gift, "streakable", False) and getattr(event, "streaking", False):
                return
            nick, uid = who(event)
            post("ChatGift", user=nick or uid, handle=uid,
                 gift=getattr(gift, "name", "") or "gift",
                 count=int(getattr(event, "repeat_count", 1) or 1))

        try:
            client.run()
        except Exception as ex:
            print(f"CHAT {type(ex).__name__}: {str(ex)[:120]}", flush=True)
        print("CHAT retrying in 20 s (is the live running?)", flush=True)
        time.sleep(20)


if __name__ == "__main__":
    sys.stdout.reconfigure(line_buffering=True)
    main()
