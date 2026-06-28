using System.Collections.Generic;
using System.ComponentModel;

namespace SwiftClean.Helpers
{
    /// <summary>
    /// Runtime localization. Bind in XAML as <c>{Binding [key], Source={StaticResource Loc}}</c>;
    /// code uses <c>Loc.Instance["key"]</c>. English is the default. Switching language raises the
    /// indexer change so every binding refreshes, and fires <see cref="LanguageChanged"/> for view models.
    /// </summary>
    public class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new();

        private string _lang = "en";

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? LanguageChanged;

        private Loc() { }

        public string Language => _lang;
        public bool IsEnglish => _lang == "en";
        public bool IsRussian => _lang == "ru";

        // Computed (not cached) so static-field init order can't leave it null.
        private Dictionary<string, string> Current => _lang == "ru" ? Ru : En;

        public string this[string key] => Current.TryGetValue(key, out var v) ? v : key;

        public void SetLanguage(string lang)
        {
            var next = lang == "ru" ? "ru" : "en";
            if (next == _lang)
                return;

            _lang = next;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRussian)));
            LanguageChanged?.Invoke();
        }

        private static readonly Dictionary<string, string> En = new()
        {
            // nav / shell
            ["nav.dashboard"] = "Dashboard", ["nav.cleaning"] = "Cleanup", ["nav.registry"] = "Registry",
            ["nav.startup"] = "Startup", ["nav.apps"] = "Apps", ["nav.disk"] = "Disk",
            ["nav.scheduler"] = "Scheduler", ["nav.settings"] = "Settings",
            ["sec.main"] = "MAIN", ["sec.tools"] = "TOOLS", ["sec.other"] = "OTHER",
            ["support.title"] = "Support", ["support.sub"] = "the developer",
            ["support.popupTitle"] = "Support the developer", ["support.name"] = "Viacheslav", ["support.role"] = "Author of SwiftClean",
            ["support.body"] = "SwiftClean is free — no ads, no subscriptions. If it helped you, any support keeps it growing.",
            ["support.cta"] = "Support the project", ["support.thanks"] = "Any amount matters — thank you 🙏",
            ["story.p1a"] = "Hi! I've never liked apps that ",
            ["story.p1b"] = "annoy you with ads, subscriptions and pop-ups",
            ["story.p1c"] = ". Companies abuse this far too often — and our favorite apps turn into irritants.",
            ["story.p2a"] = "I made SwiftClean ",
            ["story.p2b"] = "completely free",
            ["story.p2c"] = " — no subscriptions, no limits. I just want you to enjoy it.",
            ["story.p3a"] = "I don't earn a cent from this app. If you can support it with ",
            ["story.p3b"] = "a dollar for a cup of coffee",
            ["story.p3c"] = " — I'd be very grateful ☕",
            ["status.ready"] = "Ready", ["status.os"] = "Windows · x64",
            ["btn.scan"] = "Scan", ["btn.scanning"] = "Scanning...",
            // page titles
            ["title.dashboard"] = "Dashboard", ["title.cleaning"] = "Disk Cleanup", ["title.registry"] = "Registry",
            ["title.startup"] = "Startup", ["title.apps"] = "Apps", ["title.disk"] = "Disk Analysis",
            ["title.scheduler"] = "Scheduler", ["title.settings"] = "Settings",
            // scan modal
            ["scan.title"] = "Analyzing system", ["scan.sub"] = "This takes about 3 seconds",
            ["stage.temp"] = "Temporary files", ["stage.recycle"] = "Recycle Bin", ["stage.cache"] = "Browser cache",
            ["stage.cookies"] = "Browser cookies", ["stage.updates"] = "Update cache",
            // categories
            ["cat.temp"] = "Temporary files", ["cat.recycle"] = "Recycle Bin", ["cat.cache"] = "Browser cache",
            ["cat.cookies"] = "Browser cookies", ["cat.updates"] = "Update cache",
            ["fmt.files"] = "{0:N0} files", ["fmt.browsers"] = "{0} of {1} browsers",
            ["fmt.more"] = "…and {0:N0} more files",
            ["cookies.hint"] = "Close the browser to clear cookies completely",
            ["unit.sec"] = "s",
            ["folder.videos"] = "Videos", ["folder.pictures"] = "Pictures", ["folder.music"] = "Music",
            ["folder.documents"] = "Documents", ["folder.downloads"] = "Downloads", ["folder.desktop"] = "Desktop",
            // dashboard
            ["dash.junk"] = "JUNK FOUND", ["dash.registry"] = "REGISTRY", ["dash.startup"] = "STARTUP", ["dash.free"] = "FREE",
            ["dash.junkSub"] = "temporary files", ["dash.regSub"] = "problem keys", ["dash.startupSub"] = "boot delay", ["dash.freeSub"] = "of 512 GB · C:",
            ["dash.found"] = "Found junk", ["dash.selectedFmt"] = "{0} selected",
            ["dash.empty1"] = "Click \"Scan\"", ["dash.empty2"] = "to analyze the system",
            ["dash.cleanFmt"] = "Clean {0}", ["dash.cleaned"] = "Cleaned successfully",
            ["dash.diskUsage"] = "Disk C: usage", ["dash.occupied"] = "OCCUPIED", ["dash.freeUp"] = "FREE",
            ["dash.lastClean"] = "Last cleanup", ["dash.never"] = "Never",
            ["dash.recommend"] = "It is recommended to clean the system once a week.",
            // cleaning
            ["clean.types"] = "Cleanup types", ["clean.empty1"] = "Run a scan first",
            ["clean.empty2"] = "Press \"Scan\" in the top bar", ["clean.cleanedFmt"] = "Cleaned successfully — {0}",
            // registry
            ["reg.problemKeys"] = "Problem keys", ["reg.foundFmt"] = "Found {0:N0} obsolete entries",
            ["reg.runScan"] = "Run a scan", ["reg.details"] = "Key details",
            ["reg.runScanAnalyze"] = "Run a scan to analyze the registry", ["reg.clean"] = "No obsolete entries found",
            ["reg.path"] = "REGISTRY PATH", ["reg.key"] = "KEY", ["reg.value"] = "CURRENT VALUE", ["reg.problem"] = "PROBLEM",
            ["reg.deleteBtn"] = "Delete entry", ["reg.deleted"] = "Deleted",
            ["reg.issue"] = "The app was removed, but the registry entry remains.",
            ["reg.removed"] = "Entry removed from the registry.",
            ["reg.statusObsolete"] = "Obsolete entry", ["reg.statusDeleted"] = "Deleted",
            ["reg.confirmTitle"] = "Delete registry entry", ["reg.confirmFmt"] = "Delete the obsolete entry «{0}» from the registry?\n\n{1}\n\nThis cannot be undone.",
            ["reg.adminTitle"] = "Could not delete", ["reg.adminMsg"] = "Administrator rights are required to delete this entry. Run SwiftClean as administrator.",
            // startup
            ["st.enabled"] = "ENABLED", ["st.delay"] = "BOOT DELAY", ["st.high"] = "HIGH IMPACT",
            ["st.program"] = "PROGRAM", ["st.impact"] = "IMPACT", ["st.ms"] = "+MS",
            ["st.enabledFmt"] = "{0} of {1}", ["st.highFmt"] = "{0} apps",
            ["impact.high"] = "High", ["impact.med"] = "Medium", ["impact.low"] = "Low",
            ["st.adminTitle"] = "Couldn't change", ["st.adminMsg"] = "Administrator rights are required to change this startup entry. Run SwiftClean as administrator.",
            // apps
            ["apps.search"] = "Search apps...", ["apps.name"] = "NAME", ["apps.size"] = "SIZE",
            ["apps.installedCol"] = "INSTALLED", ["apps.usedCol"] = "USED", ["apps.remove"] = "Remove",
            ["apps.storage"] = "Storage", ["apps.installed"] = "INSTALLED", ["apps.installedFmt"] = "{0:N0} programs",
            ["apps.occupy"] = "OCCUPY ON DISK", ["apps.freeOnC"] = "FREE ON C:", ["apps.refresh"] = "Refresh list",
            ["apps.uninstallTitle"] = "Uninstall program", ["apps.uninstallFmt"] = "Start removal of «{0}»?\n\nThe program's own uninstaller will open.",
            ["apps.errTitle"] = "Error", ["apps.errMsg"] = "Could not start this program's uninstaller.",
            ["apps.removeBtn"] = "Remove", ["apps.cancel"] = "Cancel",
            // disk
            ["disk.occupiedFmt"] = "Occupied {0}", ["disk.freeOfFmt"] = "free of {0}",
            ["disk.occupiedPctFmt"] = "Occupied {0:0}%", ["disk.freePctFmt"] = "Free {0:0}%",
            ["disk.usageMap"] = "Usage map", ["disk.largeFolders"] = "Large folders", ["disk.openFolder"] = "Open folder", ["disk.drive"] = "Drive",
            ["disk.analyzing"] = "Analyzing disk…", ["disk.analyzingFmt"] = "Analyzing disk… {0}",
            ["disk.notAnalyzed"] = "Open the Disk section to analyze usage",
            ["disktype.video"] = "Video", ["disktype.photo"] = "Photos", ["disktype.audio"] = "Audio",
            ["disktype.documents"] = "Documents", ["disktype.archives"] = "Archives", ["disktype.apps"] = "Apps",
            ["disktype.other"] = "Other", ["disktype.system"] = "System", ["disktype.free"] = "Free",
            ["disk.appMapFmt"] = "{0} apps · by size", ["disk.noApps"] = "No app size data available",
            ["disk.usedNever"] = "Not used recently", ["disk.usedToday"] = "Used today",
            ["disk.usedDaysFmt"] = "Used {0}d ago", ["disk.usedMonthsFmt"] = "Used ~{0}mo ago",
            ["disk.legendRecent"] = "Used recently", ["disk.legendMonths"] = "Months ago",
            ["disk.legendStale"] = "Long unused", ["disk.legendUnknown"] = "Unknown",
            // scheduler
            ["sch.autoClean"] = "Auto-clean", ["sch.autoCleanSub"] = "Run cleanup on a schedule",
            ["sch.frequency"] = "Frequency", ["sch.daily"] = "Daily", ["sch.weekly"] = "Weekly", ["sch.monthly"] = "Monthly",
            ["sch.time"] = "Run time", ["sch.timeNote"] = "— while the computer is idle",
            ["sch.what"] = "What to clean", ["sch.temp"] = "Temporary files", ["sch.recycle"] = "Recycle Bin",
            ["sch.cache"] = "Browser cache", ["sch.save"] = "Save schedule",
            ["sch.savedTitle"] = "Schedule saved", ["sch.savedMsg"] = "Auto-clean will run on the chosen schedule at 03:00.",
            ["sch.errTitle"] = "Couldn't save", ["sch.errMsg"] = "Could not create the scheduled task. Try running SwiftClean as administrator.",
            // settings
            ["set.language"] = "INTERFACE LANGUAGE", ["set.theme"] = "THEME", ["set.dark"] = "Dark", ["set.light"] = "Light",
            ["set.general"] = "GENERAL", ["set.notif"] = "Notifications", ["set.notifSub"] = "Show cleanup results",
            ["set.autostart"] = "Start with Windows", ["set.autostartSub"] = "Launch SwiftClean at login",
            ["set.about"] = "ABOUT", ["set.aboutText"] = "A free Windows system cleaner with no subscriptions or locked features.",
            ["set.version"] = "Version 1.0.0 · Windows 10/11 · .NET 8",
            // dialogs (clean)
            ["dlg.cleanTitle"] = "Confirm cleanup",
            ["dlg.cleanFmt"] = "Move the selected categories to the Recycle Bin?\n\n{0}\n\nAbout {1} will be freed. Files can be restored from the Recycle Bin.",
            ["dlg.recycleFmt"] = "Empty the Recycle Bin? About {0} will be removed.\n\nThis is permanent — files cannot be restored.",
            ["dlg.clean"] = "Clean", ["dlg.cleanRecycle"] = "Empty Recycle Bin", ["dlg.cancel"] = "Cancel",
            ["dlg.doneTitle"] = "Cleanup complete",
            ["dlg.doneFmt"] = "Freed {0}. Files were moved to the Recycle Bin.\n\nOpen the Recycle Bin to view them?",
            ["dlg.openRecycle"] = "Open Recycle Bin", ["dlg.close"] = "Close",
            ["btn.delete"] = "Delete entry", ["btn.understand"] = "Got it",
        };

        private static readonly Dictionary<string, string> Ru = new()
        {
            ["nav.dashboard"] = "Dashboard", ["nav.cleaning"] = "Очистка", ["nav.registry"] = "Реестр",
            ["nav.startup"] = "Автозагрузка", ["nav.apps"] = "Приложения", ["nav.disk"] = "Диск",
            ["nav.scheduler"] = "Планировщик", ["nav.settings"] = "Настройки",
            ["sec.main"] = "ГЛАВНОЕ", ["sec.tools"] = "ИНСТРУМЕНТЫ", ["sec.other"] = "ПРОЧЕЕ",
            ["support.title"] = "Поддержать", ["support.sub"] = "разработчика",
            ["support.popupTitle"] = "Поддержать разработчика", ["support.name"] = "Viacheslav", ["support.role"] = "Автор SwiftClean",
            ["support.body"] = "SwiftClean бесплатный — без рекламы и подписок. Если приложение вам помогло, любая поддержка помогает развивать его дальше.",
            ["support.cta"] = "Поддержать проект", ["support.thanks"] = "Любая сумма имеет значение — спасибо 🙏",
            ["story.p1a"] = "Здравствуйте! Мне никогда не нравились приложения, которые ",
            ["story.p1b"] = "бесят рекламой, подписками и всплывающими окнами",
            ["story.p1c"] = ". Компании очень часто злоупотребляют этим — и наши любимые приложения превращаются в раздражители.",
            ["story.p2a"] = "Я сделал SwiftClean ",
            ["story.p2b"] = "полностью бесплатным",
            ["story.p2c"] = " — без подписок и ограничений. Хочу, чтобы вы просто наслаждались им.",
            ["story.p3a"] = "Я не получаю ни копейки с этого приложения. Если можете поддержать ",
            ["story.p3b"] = "долларом на чашку кофе",
            ["story.p3c"] = " — буду очень благодарен ☕",
            ["status.ready"] = "Готов к работе", ["status.os"] = "Windows · x64",
            ["btn.scan"] = "Сканировать", ["btn.scanning"] = "Сканирую...",
            ["title.dashboard"] = "Dashboard", ["title.cleaning"] = "Очистка диска", ["title.registry"] = "Реестр",
            ["title.startup"] = "Автозагрузка", ["title.apps"] = "Приложения", ["title.disk"] = "Анализ диска",
            ["title.scheduler"] = "Планировщик", ["title.settings"] = "Настройки",
            ["scan.title"] = "Анализ системы", ["scan.sub"] = "Это займёт около 3 секунд",
            ["stage.temp"] = "Временные файлы", ["stage.recycle"] = "Корзина", ["stage.cache"] = "Кэш браузеров",
            ["stage.cookies"] = "Cookies браузеров", ["stage.updates"] = "Кэш обновлений",
            ["cat.temp"] = "Временные файлы", ["cat.recycle"] = "Корзина", ["cat.cache"] = "Кэш браузеров",
            ["cat.cookies"] = "Cookies браузеров", ["cat.updates"] = "Кэш обновлений",
            ["fmt.files"] = "{0:N0} файлов", ["fmt.browsers"] = "{0} из {1} браузеров",
            ["fmt.more"] = "…и ещё {0:N0} файлов",
            ["cookies.hint"] = "Закройте браузер, чтобы кэш удалился полностью",
            ["unit.sec"] = "с",
            ["folder.videos"] = "Видео", ["folder.pictures"] = "Изображения", ["folder.music"] = "Музыка",
            ["folder.documents"] = "Документы", ["folder.downloads"] = "Загрузки", ["folder.desktop"] = "Рабочий стол",
            ["dash.junk"] = "МУСОР НАЙДЕН", ["dash.registry"] = "РЕЕСТР", ["dash.startup"] = "АВТОЗАГРУЗКА", ["dash.free"] = "СВОБОДНО",
            ["dash.junkSub"] = "временных файлов", ["dash.regSub"] = "проблемных ключей", ["dash.startupSub"] = "задержка запуска", ["dash.freeSub"] = "из 512 GB · C:",
            ["dash.found"] = "Найденный мусор", ["dash.selectedFmt"] = "{0} выбрано",
            ["dash.empty1"] = "Нажмите «Сканировать»", ["dash.empty2"] = "для анализа системы",
            ["dash.cleanFmt"] = "Очистить {0}", ["dash.cleaned"] = "Очищено успешно",
            ["dash.diskUsage"] = "Использование диска C:", ["dash.occupied"] = "ЗАНЯТО", ["dash.freeUp"] = "СВОБОДНО",
            ["dash.lastClean"] = "Последняя очистка", ["dash.never"] = "Никогда",
            ["dash.recommend"] = "Рекомендуется выполнять очистку системы раз в неделю.",
            ["clean.types"] = "Типы очистки", ["clean.empty1"] = "Сначала запустите сканирование",
            ["clean.empty2"] = "Нажмите «Сканировать» в верхней панели", ["clean.cleanedFmt"] = "Очищено успешно — {0}",
            ["reg.problemKeys"] = "Проблемные ключи", ["reg.foundFmt"] = "Найдено {0:N0} устаревших записей",
            ["reg.runScan"] = "Запустите сканирование", ["reg.details"] = "Детали ключа",
            ["reg.runScanAnalyze"] = "Запустите сканирование для анализа реестра", ["reg.clean"] = "Устаревших записей не найдено",
            ["reg.path"] = "ПУТЬ В РЕЕСТРЕ", ["reg.key"] = "КЛЮЧ", ["reg.value"] = "ТЕКУЩЕЕ ЗНАЧЕНИЕ", ["reg.problem"] = "ПРОБЛЕМА",
            ["reg.deleteBtn"] = "Удалить запись", ["reg.deleted"] = "Удалено",
            ["reg.issue"] = "Приложение было удалено, но запись осталась в реестре.",
            ["reg.removed"] = "Запись удалена из реестра.",
            ["reg.statusObsolete"] = "Устаревшая запись", ["reg.statusDeleted"] = "Удалено",
            ["reg.confirmTitle"] = "Удаление записи реестра", ["reg.confirmFmt"] = "Удалить устаревшую запись «{0}» из реестра?\n\n{1}\n\nЭто действие необратимо.",
            ["reg.adminTitle"] = "Не удалось удалить", ["reg.adminMsg"] = "Для удаления этой записи нужны права администратора. Запустите SwiftClean от имени администратора.",
            ["st.enabled"] = "ВКЛЮЧЕНО", ["st.delay"] = "ЗАДЕРЖКА ЗАПУСКА", ["st.high"] = "ВЫСОКОЕ ВЛИЯНИЕ",
            ["st.program"] = "ПРОГРАММА", ["st.impact"] = "ВЛИЯНИЕ", ["st.ms"] = "+МС",
            ["st.enabledFmt"] = "{0} из {1}", ["st.highFmt"] = "{0} прогр.",
            ["impact.high"] = "Высокое", ["impact.med"] = "Среднее", ["impact.low"] = "Низкое",
            ["st.adminTitle"] = "Не удалось изменить", ["st.adminMsg"] = "Для изменения этой записи автозагрузки нужны права администратора. Запустите SwiftClean от имени администратора.",
            ["apps.search"] = "Поиск приложений...", ["apps.name"] = "НАЗВАНИЕ", ["apps.size"] = "РАЗМЕР",
            ["apps.installedCol"] = "УСТАНОВЛЕН", ["apps.usedCol"] = "ИСПОЛЬЗОВАН", ["apps.remove"] = "Удалить",
            ["apps.storage"] = "Хранилище", ["apps.installed"] = "УСТАНОВЛЕНО", ["apps.installedFmt"] = "{0:N0} программ",
            ["apps.occupy"] = "ЗАНИМАЮТ НА ДИСКЕ", ["apps.freeOnC"] = "СВОБОДНО НА C:", ["apps.refresh"] = "Обновить список",
            ["apps.uninstallTitle"] = "Удаление программы", ["apps.uninstallFmt"] = "Запустить удаление «{0}»?\n\nОткроется штатная программа удаления.",
            ["apps.errTitle"] = "Ошибка", ["apps.errMsg"] = "Не удалось запустить программу удаления этой программы.",
            ["apps.removeBtn"] = "Удалить", ["apps.cancel"] = "Отмена",
            ["disk.occupiedFmt"] = "Занято {0}", ["disk.freeOfFmt"] = "свободно из {0}",
            ["disk.occupiedPctFmt"] = "Занято {0:0}%", ["disk.freePctFmt"] = "Свободно {0:0}%",
            ["disk.usageMap"] = "Карта использования", ["disk.largeFolders"] = "Крупные папки", ["disk.openFolder"] = "Открыть папку", ["disk.drive"] = "Диск",
            ["disk.analyzing"] = "Анализ диска…", ["disk.analyzingFmt"] = "Анализ диска… {0}",
            ["disk.notAnalyzed"] = "Откройте раздел «Диск» для анализа",
            ["disktype.video"] = "Видео", ["disktype.photo"] = "Фото", ["disktype.audio"] = "Аудио",
            ["disktype.documents"] = "Документы", ["disktype.archives"] = "Архивы", ["disktype.apps"] = "Приложения",
            ["disktype.other"] = "Прочее", ["disktype.system"] = "Система", ["disktype.free"] = "Свободно",
            ["disk.appMapFmt"] = "{0} прил. · по размеру", ["disk.noApps"] = "Нет данных о размере приложений",
            ["disk.usedNever"] = "Давно не использовалось", ["disk.usedToday"] = "Использовалось сегодня",
            ["disk.usedDaysFmt"] = "Использовалось {0} дн. назад", ["disk.usedMonthsFmt"] = "Использовалось ~{0} мес назад",
            ["disk.legendRecent"] = "Недавно", ["disk.legendMonths"] = "Месяцы назад",
            ["disk.legendStale"] = "Давно", ["disk.legendUnknown"] = "Неизвестно",
            ["sch.autoClean"] = "Автоочистка", ["sch.autoCleanSub"] = "Запускать очистку по расписанию",
            ["sch.frequency"] = "Частота", ["sch.daily"] = "Ежедневно", ["sch.weekly"] = "Еженедельно", ["sch.monthly"] = "Ежемесячно",
            ["sch.time"] = "Время запуска", ["sch.timeNote"] = "— пока компьютер не используется",
            ["sch.what"] = "Что очищать", ["sch.temp"] = "Временные файлы", ["sch.recycle"] = "Корзина",
            ["sch.cache"] = "Кэш браузеров", ["sch.save"] = "Сохранить расписание",
            ["sch.savedTitle"] = "Расписание сохранено", ["sch.savedMsg"] = "Автоочистка будет запускаться по расписанию в 03:00.",
            ["sch.errTitle"] = "Не удалось сохранить", ["sch.errMsg"] = "Не удалось создать задачу в планировщике. Попробуйте запустить SwiftClean от имени администратора.",
            ["set.language"] = "ЯЗЫК ИНТЕРФЕЙСА", ["set.theme"] = "ТЕМА ОФОРМЛЕНИЯ", ["set.dark"] = "Тёмная", ["set.light"] = "Светлая",
            ["set.general"] = "ОБЩИЕ", ["set.notif"] = "Уведомления", ["set.notifSub"] = "Показывать результаты очистки",
            ["set.autostart"] = "Автозапуск с Windows", ["set.autostartSub"] = "Запускать SwiftClean при входе",
            ["set.about"] = "О ПРОГРАММЕ", ["set.aboutText"] = "Бесплатный чистильщик системы Windows без подписок и ограничений функций.",
            ["set.version"] = "Версия 1.0.0 · Windows 10/11 · .NET 8",
            ["dlg.cleanTitle"] = "Подтверждение очистки",
            ["dlg.cleanFmt"] = "Переместить в Корзину выбранные категории?\n\n{0}\n\nБудет освобождено примерно {1}. Файлы можно будет восстановить из Корзины.",
            ["dlg.recycleFmt"] = "Очистить Корзину? Будет удалено примерно {0}.\n\nЭто действие необратимо — файлы нельзя будет восстановить.",
            ["dlg.clean"] = "Очистить", ["dlg.cleanRecycle"] = "Очистить Корзину", ["dlg.cancel"] = "Отмена",
            ["dlg.doneTitle"] = "Очистка завершена",
            ["dlg.doneFmt"] = "Освобождено {0}. Файлы перемещены в Корзину.\n\nОткрыть Корзину, чтобы посмотреть их?",
            ["dlg.openRecycle"] = "Открыть Корзину", ["dlg.close"] = "Закрыть",
            ["btn.delete"] = "Удалить запись", ["btn.understand"] = "Понятно",
        };
    }
}
