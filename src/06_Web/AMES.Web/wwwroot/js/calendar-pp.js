'use strict';
window.ppCal = (() => {
    let _cal = null;
    let _ref = null;

    return {
        init(dotNetRef, events, view) {
            _ref = dotNetRef;
            const el = document.getElementById('pp-cal-fc');
            if (!el || !window.FullCalendar) return;
            if (_cal) { _cal.destroy(); _cal = null; }

            _cal = new FullCalendar.Calendar(el, {
                initialView: view === 'week' ? 'timeGridWeek' : 'dayGridMonth',
                locale: 'ko',
                height: 680,
                headerToolbar: {
                    left: 'prev,next today',
                    center: 'title',
                    right: ''
                },
                editable: true,
                dayMaxEvents: 5,
                eventDidMount(info) {
                    info.el.title = info.event.extendedProps.tip ?? '';
                },
                eventDrop(info) {
                    const newDate = info.event.start.toISOString().slice(0, 10);
                    info.revert(); // always revert; Blazor confirms after PIN
                    _ref.invokeMethodAsync('OnEventDrop', info.event.id, newDate);
                },
                eventClick(info) {
                    _ref.invokeMethodAsync('OnEventClick', info.event.id);
                }
            });

            if (events && events.length) _cal.addEventSource(events);
            _cal.render();
        },

        changeView(view) {
            if (_cal) _cal.changeView(view === 'week' ? 'timeGridWeek' : 'dayGridMonth');
        },

        refresh(events) {
            if (!_cal) return;
            _cal.removeAllEvents();
            if (events && events.length) _cal.addEventSource(events);
        },

        destroy() {
            if (_cal) { _cal.destroy(); _cal = null; }
        }
    };
})();
