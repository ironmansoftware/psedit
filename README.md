## PSEdit

Edit PowerShell scripts directly in your terminal. 

![](./screenshot.png)

- IntelliSense
- Syntax Higlighting
- Format on Save
- Script Execution
- Error View
- Syntax Error View

## Installation

```powershell
Install-Module psedit
```

## Editing

To start the editor, you can simply call `Show-PSEditor` in a terminal.

```powershell
Show-PSEditor
```

You can open a file by using the `-Path` parameter.

```powershell
Show-PSEditor -Path .\file.path
```

### Syntax Errors

Syntax errors will be shown in the editor by a red highlight. To view the text of the syntax error, click View \ Syntax Errors.

### Formatting

You can format your code in the editor if you have `PSScriptAnalyzer` installed. To format a script, either press `Ctrl+Shift+R` or click Edit \ Format. If you don't have `PSScriptAnalyzer` installed, you can do so with the command below.

```powershell
Install-Module PSScriptAnalyzer
```

## Execution

To execute your script, press `F5` to run the entire script. If you want to execute a select, you can press `F8`. You can also execute the script in the terminal and exit the editor by pressing `Ctrl+Shift+F5`.

You can also use the Debug menu to access these options.

### Errors

Errors generated when running scripts will be shown in the error window. You can access it by clicking View \ Errors.