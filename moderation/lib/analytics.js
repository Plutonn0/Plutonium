// UTC calendar days keep chart buckets and weekly comparisons consistent.
export function dailySeries(activity, installs, now = new Date()) {
  const key = value => new Date(value).toISOString().slice(0, 10);
  const active = new Map(activity.map(row => [key(row.day), Number(row.count)]));
  const added = new Map(installs.map(row => [key(row.day), Number(row.count)]));
  const today = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  return Array.from({length: 14}, (_, index) => {
    const day = key(today - (13 - index) * 86400000);
    return { day, active: active.get(day) ?? 0, installs: added.get(day) ?? 0 };
  });
}
