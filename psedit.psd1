
@{
    RootModule      = 'psedit.dll'
    ModuleVersion   = '1.1.0'
    GUID            = 'beb23c4b-3e9a-4be3-ad4e-0c38d448353c'
    Author          = 'Adam Driscoll'
    CompanyName     = 'Ironman Software'
    Copyright       = '(c) Ironman Software. All rights reserved.'
    Description     = 'Terminal-based editor for PowerShell'
    CmdletsToExport = 'Show-PSEditor'
    AliasesToExport   = 'psedit'
    PrivateData     = @{
        PSData = @{
            Tags       = @('editor', 'tui')
            LicenseUri = 'https://github.com/ironmansoftware/psedit/LICENSE'
            ProjectUri = 'https://github.com/ironmansoftware/psedit'
        }
    }
}

