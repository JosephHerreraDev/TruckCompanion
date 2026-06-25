import { Clock, Fuel, Gauge, MapPinned, RadioTower, TriangleAlert } from "lucide-react";
import type { ReactNode } from "react";
import type { TelemetrySnapshot } from "../types";

type Props = {
  snapshot: TelemetrySnapshot | null;
  streamError: string | null;
};

export function StatusHud({ snapshot, streamError }: Props) {
  const connected = Boolean(snapshot?.game.connected && !snapshot.connection.stale && !streamError);

  return (
    <aside className="status-hud compact-hud" aria-label="Telemetry status">
      {streamError || snapshot?.connection.error ? (
        <div className="alert-line">
          <TriangleAlert size={16} />
          {streamError ?? snapshot?.connection.error}
        </div>
      ) : null}

      <section className="job-strip">
        <div className="destination-line">
          <MapPinned size={18} />
          <div>
            <strong>To: {snapshot?.job.destination.company || snapshot?.job.destination.city || "No destination"}</strong>
            <span>{snapshot?.job.destination.city || "Waiting for job"} · {snapshot?.job.cargo || "No cargo"}</span>
          </div>
        </div>
        <div className="strip-metrics">
          <Metric icon={<Clock size={16} />} label="ETA" value={snapshot?.job.eta || "--:--"} />
          <Metric icon={<Fuel size={16} />} label="Fuel" value={`${Math.round(snapshot?.truck.fuelLiters ?? 0)} L`} />
          <Metric icon={<Gauge size={16} />} label="Damage" value={`${(snapshot?.truck.maxVehicleDamagePercent ?? 0).toFixed(1)}%`} />
          <span className={connected ? "status-pill online" : "status-pill offline"}>
            <RadioTower size={13} />
            {connected ? "live" : "offline"}
          </span>
        </div>
      </section>
    </aside>
  );
}

function Metric({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="metric">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
