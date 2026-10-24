import { useEffect, useRef, useState } from "react";
import jsQR from "jsqr";
import { CameraOff } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  /** Fires once per freshly-seen code — the same code decoded across consecutive frames is debounced, not resent. */
  onDecode: (text: string) => void;
  /** Keeps the camera stream alive but stops feeding frames to the decoder — used while a redeem request is in flight or a result banner is showing, so the gate doesn't fire the same scan twice. */
  paused?: boolean;
  className?: string;
}

/**
 * Continuous back-camera QR scanner for the feast gate. Pure browser APIs —
 * getUserMedia + canvas + jsQR, no native dependency and no server round
 * trip just to decode a frame — the server only ever sees the resulting
 * code string via the redeem call the containing page makes.
 */
export function QrScanner({ onDecode, paused = false, className }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const frameRef = useRef<number>(0);
  const pausedRef = useRef(paused);
  const onDecodeRef = useRef(onDecode);
  const lastDecodedRef = useRef<{ text: string; at: number }>({ text: "", at: 0 });
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { pausedRef.current = paused; }, [paused]);
  useEffect(() => { onDecodeRef.current = onDecode; }, [onDecode]);

  useEffect(() => {
    let cancelled = false;

    function tick() {
      frameRef.current = requestAnimationFrame(tick);
      const video = videoRef.current;
      const canvas = canvasRef.current;
      if (!video || !canvas || pausedRef.current) return;
      if (video.readyState !== video.HAVE_ENOUGH_DATA) return;

      canvas.width = video.videoWidth;
      canvas.height = video.videoHeight;
      const ctx = canvas.getContext("2d");
      if (!ctx) return;
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

      let frame: ImageData;
      try {
        frame = ctx.getImageData(0, 0, canvas.width, canvas.height);
      } catch {
        return; // canvas not painted yet on this tick
      }

      const result = jsQR(frame.data, frame.width, frame.height);
      if (!result?.data) return;

      const now = Date.now();
      const last = lastDecodedRef.current;
      if (result.data === last.text && now - last.at < 2500) return; // same code, still in view — don't resend
      lastDecodedRef.current = { text: result.data, at: now };
      onDecodeRef.current(result.data);
    }

    async function start() {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: "environment" } },
          audio: false,
        });
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop());
          return;
        }
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          await videoRef.current.play();
        }
        tick();
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Camera unavailable");
      }
    }

    start();
    return () => {
      cancelled = true;
      cancelAnimationFrame(frameRef.current);
      streamRef.current?.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    };
  }, []);

  if (error) {
    return (
      <div className={cn("flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-border bg-muted/40 p-8 text-center", className)}>
        <CameraOff className="h-8 w-8 text-muted-foreground" />
        <p className="text-sm font-medium">Camera unavailable</p>
        <p className="max-w-xs text-xs text-muted-foreground">{error} — use the code field below instead.</p>
      </div>
    );
  }

  return (
    <div className={cn("relative overflow-hidden rounded-xl bg-black", className)}>
      {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
      <video ref={videoRef} muted playsInline className="h-full w-full object-cover" />
      <canvas ref={canvasRef} className="hidden" />
      <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
        <div className="h-2/3 w-2/3 rounded-2xl border-2 border-white/70" />
      </div>
    </div>
  );
}
