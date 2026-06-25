import { useCallback, useEffect, useRef, useState } from "react";

type WakeLockSentinel = {
  released: boolean;
  release: () => Promise<void>;
  addEventListener: (type: "release", listener: () => void) => void;
  removeEventListener: (type: "release", listener: () => void) => void;
};

type WakeLockNavigator = Navigator & {
  wakeLock?: {
    request: (type: "screen") => Promise<WakeLockSentinel>;
  };
};

export type WakeLockState = "unsupported" | "inactive" | "active" | "error";

export function useWakeLock(enabled: boolean) {
  const sentinelRef = useRef<WakeLockSentinel | null>(null);
  const [state, setState] = useState<WakeLockState>(() => (supportsWakeLock() ? "inactive" : "unsupported"));

  const release = useCallback(async () => {
    const sentinel = sentinelRef.current;
    sentinelRef.current = null;

    if (sentinel && !sentinel.released) {
      await sentinel.release();
    }

    setState(supportsWakeLock() ? "inactive" : "unsupported");
  }, []);

  const request = useCallback(async () => {
    const wakeLock = (navigator as WakeLockNavigator).wakeLock;
    if (!wakeLock) {
      setState("unsupported");
      return;
    }

    if (document.visibilityState !== "visible") {
      return;
    }

    try {
      if (sentinelRef.current && !sentinelRef.current.released) {
        setState("active");
        return;
      }

      const sentinel = await wakeLock.request("screen");
      const handleRelease = () => {
        sentinel.removeEventListener("release", handleRelease);
        if (sentinelRef.current === sentinel) {
          sentinelRef.current = null;
        }
        setState(supportsWakeLock() ? "inactive" : "unsupported");
      };

      sentinel.addEventListener("release", handleRelease);
      sentinelRef.current = sentinel;
      setState("active");
    } catch {
      sentinelRef.current = null;
      setState("error");
    }
  }, []);

  useEffect(() => {
    if (!enabled) {
      void release();
      return;
    }

    void request();

    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        void request();
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      void release();
    };
  }, [enabled, release, request]);

  return {
    state,
    supported: state !== "unsupported",
    active: state === "active",
    request,
    release
  };
}

function supportsWakeLock() {
  return "wakeLock" in navigator;
}
