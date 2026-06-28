using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>
    /// One installed device driver from <c>Win32_PnPSignedDriver</c>.
    /// Driver date alone cannot reliably indicate whether an update exists — the real check runs through
    /// the Windows Update Agent during a scan, which sets each device to up-to-date or update-available.
    /// </summary>
    public sealed class DriverInfo : ViewModelBase
    {
        private static Loc L => Loc.Instance;

        public DriverInfo(string deviceName, string manufacturer, string deviceClass,
                          string driverVersion, DateTime? driverDate, string supportUrl,
                          string deviceId = "")
        {
            DeviceName    = deviceName;
            Manufacturer  = manufacturer;
            DeviceClass   = deviceClass;
            DriverVersion = driverVersion;
            DriverDate    = driverDate;
            SupportUrl    = supportUrl;
            DeviceId      = deviceId;
            Loc.Instance.LanguageChanged += () => OnPropertyChanged(string.Empty);
        }

        public string    DeviceName    { get; }
        public string    Manufacturer  { get; }
        public string    DeviceClass   { get; }
        public string    DriverVersion { get; }
        public DateTime? DriverDate    { get; }
        public string    SupportUrl    { get; }
        public string    DeviceId      { get; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        // True while this driver's update is downloading/installing (drives the row's spinner animation).
        private bool _isInstalling;
        public bool IsInstalling { get => _isInstalling; set => SetProperty(ref _isInstalling, value); }

        // Three honest states. Driver date alone cannot tell whether an update exists (inbox and
        // stable drivers carry old dates), so we never guess: a row is "not checked" until the user
        // runs a real Windows Update search, after which it becomes "update available" or "up to date".
        //   null  → not checked yet
        //   true  → WU found a matching update
        //   false → WU checked, no update for this device
        private bool? _wuUpdateAvailable;

        public bool WuChecked  => _wuUpdateAvailable.HasValue;
        public bool IsOutdated => _wuUpdateAvailable == true;
        public bool IsUpToDate => _wuUpdateAvailable == false;

        /// <summary>Records the outcome of a Windows Update search for this device (null = reset to unchecked).</summary>
        public void SetWuResult(bool? updateAvailable)
        {
            _wuUpdateAvailable = updateAvailable;
            OnPropertyChanged(nameof(WuChecked));
            OnPropertyChanged(nameof(IsOutdated));
            OnPropertyChanged(nameof(IsUpToDate));
            OnPropertyChanged(nameof(StatusLabel));
        }

        /// <summary>Conservative match of a WU driver update to this device, by model or update title.</summary>
        public bool MatchesWu(WuDriverUpdate u)
        {
            if (!string.IsNullOrWhiteSpace(u.DriverModel) &&
                (DeviceName.Contains(u.DriverModel, StringComparison.OrdinalIgnoreCase) ||
                 u.DriverModel.Contains(DeviceName, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!string.IsNullOrWhiteSpace(u.Title) &&
                u.Title.Contains(DeviceName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public string VersionText      => string.IsNullOrWhiteSpace(DriverVersion) ? "—" : DriverVersion;
        public string DateText         => DriverDate is { } d ? d.ToString("dd.MM.yyyy") : "—";
        public string ManufacturerText => string.IsNullOrWhiteSpace(Manufacturer)  ? "—" : Manufacturer;

        public string DeviceTypeLabel
        {
            get
            {
                var key = "drv.type." + DeviceClass.ToLowerInvariant();
                var val = L[key];
                return val == key ? DeviceClass : val;
            }
        }

        public string StatusLabel =>
            !WuChecked  ? L["drv.notchecked"] :
            IsOutdated  ? L["drv.canUpdate"]  :
                          L["drv.uptodate"];
    }
}
