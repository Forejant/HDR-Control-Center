using System;
using System.Windows.Automation;

namespace HdrCenter {
// Settings rebuilds its UIA tree during navigation and display mode changes.
// Each attempt must obtain fresh controls; only stale controls are retried.
public static class SettingsWait {
    public static T Run<T>(Func<T> read,Func<T,bool> ready,int attempts,Action pause,string failure) {
        for(int i=0;i<attempts;i++) {
            try { var value=read(); if(ready(value))return value; }
            catch(ElementNotAvailableException) { }
            if(i+1<attempts)pause();
        }
        throw new Exception(failure);
    }
    public static bool MonitorMatches(string label,string name) {
        if(String.IsNullOrWhiteSpace(label) || String.IsNullOrWhiteSpace(name))return false;
        label=label.Trim(); name=name.Trim();
        if(String.Equals(label,name,StringComparison.OrdinalIgnoreCase))return true;
        int colon=label.IndexOfAny(new[]{':','：'});
        return colon>=0 && String.Equals(label.Substring(colon+1).Trim(),name,StringComparison.OrdinalIgnoreCase);
    }
}
}
