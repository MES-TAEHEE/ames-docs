using AMES.Data.Aps.Domain;
using AMES.Data.Scheduling;

namespace AMES.Data.Aps;

/// <summary>AMES 근무일 달력(SYS_FactoryCalendar WORKDAY·SPECIAL, 행 없는 날은 토·일 휴무)을 엔진 달력으로 감싼다(스펙 §2.2).</summary>
public sealed class WorkdayCalendarAdapter(WorkdayCalendar cal) : IApsCalendar
{
    public bool IsWorkday(DateOnly d) => cal.IsWorkday(d.ToDateTime(TimeOnly.MinValue));
}
