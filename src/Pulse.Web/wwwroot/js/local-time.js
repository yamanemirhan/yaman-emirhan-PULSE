export function formatLocalTime(isoUtc) {
    const instant = new Date(isoUtc);
    const parts = new Intl.DateTimeFormat("en-GB", {
        day: "2-digit", month: "short", year: "numeric",
        hour: "2-digit", minute: "2-digit", second: "2-digit",
        hourCycle: "h23", timeZoneName: "shortOffset"
    }).formatToParts(instant);
    const value = type => parts.find(part => part.type === type).value;
    const offset = value("timeZoneName").replace("GMT", "UTC");
    return `${value("day")} ${value("month")} ${value("year")} · ${value("hour")}:${value("minute")}:${value("second")} (${offset})`;
}
