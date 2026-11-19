; Setzt MyAppVersion aus <Version> in Directory.Build.props, der einzigen Quelle der
; Versionsnummer. Zeilensuche statt XML-Parser: <Version>...</Version> muss auf einer Zeile
; stehen, und vor dem Element darf "<Version>" nicht vorkommen (auch nicht im Kommentar).
; Die CI vergleicht das Ergebnis mit dem echten XML-Wert (.github/workflows/ci.yml).

; Schrägstrich, damit ISPP keinen Backslash als Escape-Zeichen liest; Windows akzeptiert beides.
#define VersionPropsPath AddBackslash(SourcePath) + "../Directory.Build.props"
#if !FileExists(VersionPropsPath)
  #pragma message "Nicht gefunden: " + VersionPropsPath
  #error Directory.Build.props nicht gefunden (installer-Ordner muss daneben liegen)
#endif
#define MyAppVersion ""
#define VersionPropsHandle 0
#define VersionPropsLine ""
#define VersionStart 0
#define VersionEnd 0

#sub ReadVersionPropsLine
  #define VersionPropsLine = FileRead(VersionPropsHandle)
  #define VersionStart = Pos("<Version>", VersionPropsLine)
  #define VersionEnd = Pos("</Version>", VersionPropsLine)
  #define MyAppVersion = (VersionStart > 0 && VersionEnd > VersionStart) ? Copy(VersionPropsLine, VersionStart + 9, VersionEnd - VersionStart - 9) : ""
#endsub

#for {VersionPropsHandle = FileOpen(VersionPropsPath); VersionPropsHandle && !FileEof(VersionPropsHandle) && MyAppVersion == ""; 0} ReadVersionPropsLine

#if VersionPropsHandle
  #expr FileClose(VersionPropsHandle)
#endif

#pragma message "MyAppVersion " + MyAppVersion + " aus " + VersionPropsPath
#if MyAppVersion == ""
  #error Version nicht ermittelt: kein <Version>-Element in Directory.Build.props gefunden
#endif
