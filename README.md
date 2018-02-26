# System A

This is an internal Azure Functions app for processing daily behaviour reports from SIMS. It adds all incidents to a Google Sheet, manages detention dates, and sends emails to students and parents.

## Google sheets
Behaviour and absence incidents are recorded and managed on Google Sheets. The formats are as follows.

### Behaviour incidents spreadsheet

* **Expectations** - Date, Surname, Forename, Incident, Staff, Period, Class, Subject, Reg, Year, Gender, PP, SEN, StudentEmail, ParentSalutation, ParentEmail, DetentionDate, Status
* **Incidents** - Date, Surname, Forename, Incident, Staff, Period, Class, Subject, Time, Location, Reg, Year, Gender, PP, SEN, StudentEmail, ParentSalutation, ParentEmail, Comments
* **AppData** - This sheet is maintained by the app. To initialise, enter any past date in A1 and the number 1 in A2.
* **Contacts** - The year number in Column A, and corresponding HOY email address in Column B. Also a row called "Registers", with an email address for sending the tutor group detention lists for printing each day.
* **DetentionDays** - Enter all the available detention dates in Column A.
* **EmailTemplates** - This contains email templates. The first two rows are the student emails for new and rescheduled detentions. Subsequent rows are the parent emails for No Chromebook, Equipment, Missed homework, Late to lesson, Misuse of Chromebook, Mobile phone, Out of class and Swearing. Column A is email title and Column B is email body. Fields are available using double-brace masks, e.g. `{{Forename}}`

### Sixth Form absences spreadsheet

* **Absences** - Date, Student, Reg, Missed, Class, Teacher, FollowUp, Initials


## Configuration

The `key.json`, `local.settings.json` and `config.json` files need to be configured before the app is used.

## Azure Storage account

Behind the scenes, an Azure Storage account is required, containing:

* Queues: `emails` and `emailtriggers`
* Blob container: `emails`

## Calling the function
The function endpoint is hit at 3:15pm every day with a SIMS report using the following Powershell scripts:

### behaviour-script1.ps1 (on the SIMS server)

    $date = Get-Date -Format s
    $params = "C:\Behaviour\behaviour-params.xml"
    $xml = [xml](Get-Content -path $params)
    $xml.ReportParameters.Parameter.Values.Date = $date.ToString()
    $xml.Save($params)

    & "C:\Program Files\SIMS\SIMS .net\commandreporter" /user:****** /password:****** /report:"Daily behaviour export" /OUTPUT:"C:\Behaviour\behaviour-report.csv" /QUIET /PARAMFILE:"C:\Behaviour\behaviour-params.xml"

    $params = "C:\Behaviour\sixthform-registration-params.xml"
    $xml = [xml](Get-Content -path $params)
    $xml.ReportParameters.Parameter.Values.Date = $date.ToString()
    $xml.Save($params)

    & "C:\Program Files\SIMS\SIMS .net\commandreporter" /user:****** /password:****** /report:"Sixth Form registration absences" /OUTPUT:"C:\Behaviour\sixthform-registration-report.csv" /QUIET /PARAMFILE:"C:\Behaviour\sixthform-registration-params.xml"

    $params = "C:\Behaviour\sixthform-lesson-params.xml"
    $xml = [xml](Get-Content -path $params)
    $xml.ReportParameters.Parameter.Values.Date = $date.ToString()
    $xml.Save($params)

    & "C:\Program Files\SIMS\SIMS .net\commandreporter" /user:****** /password:****** /report:"Sixth Form lesson absences" /OUTPUT:"C:\Behaviour\sixthform-lesson-report.csv" /QUIET /PARAMFILE:"C:\Behaviour\sixthform-lesson-params.xml"

### behaviour-script2.ps1 (subsequently)

    # Daily behaviour export
    $csv = Get-Content "C:\Behaviour\behaviour-report.csv" -Raw
    $csv = $csv.Replace("`r`n", "[newline]").Replace("`n", " ").Replace("[newline]", "`r`n")
    $json = $csv -split '[\r\n]' | Select-Object -Skip 1 | ConvertFrom-Csv -Header "Date","Incident","Period","Class","Subject","Time","Location","Comments",
    "Surname","Forename","Reg","StudentEmail","Gender","PP","SEN","ParentSalutation","ParentEmail","Staff" | ConvertTo-Json -Compress

    if ($json -eq $null) { $json = "[]" }
    elseif(!$json.StartsWith("[")) { $json = "[" + $json + "]" }
    $json = $json -replace """PP"":""F""", """PP"":"""""
    $json = $json -replace """SEN"":""N""", """SEN"":"""""

    Invoke-RestMethod https://******.azurewebsites.net/api/processbehaviour?code=****** -ContentType "application/json" -Method POST -Body $json

    # Sixth Form registration and lesson absences
    $regJson = Get-Content -path "C:\Behaviour\sixthform-registration-report.csv" | Select-Object -Skip 1 | ConvertFrom-Csv -Header "Forename","Surname","Reg","StudentEmail","RegistrationMark","ParentSalutation","ParentEmail" | ConvertTo-Json -Compress
    $lessonJson = Get-Content -path "C:\Behaviour\sixthform-lesson-report.csv" | Select-Object -Skip 1 | ConvertFrom-Csv -Header "Forename","Surname","Reg","StudentEmail","LessonMark","Class","Subject","Teacher","ParentSalutation","ParentEmail" | ConvertTo-Json -Compress

    if ($regJson -eq $null -and $lessonJson -eq $null) {
        $json = "[]"
    } elseif ($regJson -eq $null) {
        $json = $lessonJson
    } elseif ($lessonJson -eq $null) {
        $json = $regJson
    } else {
        $json = "[" + $regJson.Substring(1, $regJson.Length - 2) + "," + $lessonJson.Substring(1, $lessonJson.Length - 2) + "]"
    }

    Invoke-RestMethod https://******.azurewebsites.net/api/processabsence?code=****** -ContentType "application/json" -Method POST -Body $json
