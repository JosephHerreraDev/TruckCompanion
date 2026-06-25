import { useEffect, useState } from "react";
import { getSnapshot, subscribeTelemetry } from "./api/client";
import { MapView } from "./components/MapView";
import { StatusHud } from "./components/StatusHud";
import type { TelemetrySnapshot } from "./types";

export function App() {
  const [snapshot, setSnapshot] = useState<TelemetrySnapshot | null>(null);
  const [streamError, setStreamError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    getSnapshot()
      .then((data) => {
        if (active) {
          setSnapshot(data);
        }
      })
      .catch((error: Error) => setStreamError(error.message));

    const unsubscribe = subscribeTelemetry(
      (data) => {
        setSnapshot(data);
        setStreamError(null);
      },
      setStreamError
    );

    return () => {
      active = false;
      unsubscribe();
    };
  }, []);

  return (
    <main className="app-shell">
      <MapView snapshot={snapshot} />
      <StatusHud snapshot={snapshot} streamError={streamError} />
    </main>
  );
}
