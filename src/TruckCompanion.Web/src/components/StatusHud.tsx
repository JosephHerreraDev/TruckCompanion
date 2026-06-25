import { Clock, Gauge, MapPinned, RadioTower, TriangleAlert } from "lucide-react";
import type { ReactNode } from "react";
import type { TelemetrySnapshot } from "../types";

type Props = {
  snapshot: TelemetrySnapshot | null;
  streamError: string | null;
};

export function StatusHud({ snapshot, streamError }: Props) {
  const connected = Boolean(snapshot?.game.connected && !snapshot.connection.stale && !streamError);
  const fuelPercent = getFuelPercent(snapshot);
  const sleepHoursLeft = snapshot?.driver.sleepHoursLeft;

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
          <FuelBar percent={fuelPercent} />
          <Metric icon={<Clock size={16} />} label="Sleep" value={formatSleep(sleepHoursLeft)} />
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

function getFuelPercent(snapshot: TelemetrySnapshot | null) {
  const fuelLiters = snapshot?.truck.fuelLiters ?? 0;
  const fuelCapacityLiters = snapshot?.truck.fuelCapacityLiters ?? 0;

  if (fuelCapacityLiters <= 0) {
    return 0;
  }

  return Math.max(0, Math.min(100, (fuelLiters / fuelCapacityLiters) * 100));
}

function formatSleep(hours: number | null | undefined) {
  if (typeof hours !== "number") {
    return "--";
  }

  return `${hours.toFixed(hours < 2 ? 1 : 0)} h`;
}

function FuelBar({ percent }: { percent: number }) {
  return (
    <div className="fuel-card">
      <div className="fuel-card__header">
        <span>Fuel</span>
        <strong>{Math.round(percent)}%</strong>
      </div>
      <div className="fuel-bar" aria-hidden="true">
        <div className="fuel-bar__fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
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
