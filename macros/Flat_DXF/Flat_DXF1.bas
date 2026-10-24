Attribute VB_Name = "Flat_DXF1"
Option Explicit

Dim swApp As SldWorks.SldWorks
Dim gFileSystemObject As Object

Const OUTPUT_FOLDER As String = "DXF"
Const EXPORT_BEND_LINES As Boolean = True
Const SMART_REBUILD As Boolean = True
Const SHOW_PROGRESS As Boolean = False

Const swDocPART As Long = 1
Const swExportSheetMetal As Long = 1
Const OPT_GEOMETRY As Long = 1
Const OPT_BEND_LINES As Long = 4

Sub main()

    On Error GoTo MainError

    Dim totalStart As Double
    Dim currentStage As String

    Dim swModel As SldWorks.ModelDoc2
    Dim swPart As SldWorks.PartDoc
    Dim swView As Object

    Dim swProgressBar As SldWorks.UserProgressBar
    Dim progressStarted As Boolean
    Dim graphicsDisabled As Boolean

    Dim initialConfigurationName As String
    Dim modelPath As String
    Dim modelFolder As String
    Dim mainOutputFolder As String
    Dim tempRoot As String
    Dim outputFolderError As String

    Dim rebuildStart As Double
    Dim preparationStart As Double
    Dim selectStart As Double
    Dim batchStart As Double
    Dim copyStart As Double

    Dim rebuildSeconds As Double
    Dim preparationSeconds As Double
    Dim selectionSeconds As Double
    Dim exportSecondsTotal As Double
    Dim copySecondsTotal As Double

    Dim needsRebuild As Boolean
    Dim rebuildCheckSucceeded As Boolean
    Dim rebuildPerformed As Boolean

    Dim exported As Long
    Dim skipped As Long
    Dim failed As Long
    Dim unknownThickness As Long
    Dim exportAttempts As Long
    Dim firstExportError As String

    Dim fso As Object
    Dim processed As Object
    Dim thicknessFolders As Object
    Dim targetPaths As Object
    Dim allocated As Object
    Dim manifest As Object
    Dim reportFile As Object

    Dim flatPatterns As New Collection
    Dim cachedBodies As New Collection
    Dim cachedCutLists As New Collection

    Dim queuedFeatures As New Collection
    Dim queuedPaths As New Collection
    Dim queuedBodies As New Collection
    Dim queuedCutLists As New Collection

    Dim flatFeat As SldWorks.Feature
    Dim cutListFeat As SldWorks.Feature
    Dim batchFeature As SldWorks.Feature
    Dim swBody As SldWorks.Body2
    Dim bodyFolder As SldWorks.bodyFolder

    Dim i As Long
    Dim qty As Long
    Dim exportOptions As Long
    Dim thicknessMm As Double

    Dim cutListKey As String
    Dim thicknessText As String
    Dim thicknessFolder As String
    Dim boxText As String
    Dim dxfName As String
    Dim finalPath As String
    Dim nativePath As String
    Dim batchPath As String

    Dim batchFiles As Collection
    Dim batchReturned As Boolean
    Dim alignment As Variant
    Dim views As Variant

    Dim totalSecondsValue As Double
    Dim averageExportSeconds As Double
    Dim resultText As String

    totalStart = Timer
    currentStage = "Starting macro"

    Set swApp = Application.SldWorks
    Set swModel = swApp.ActiveDoc

    If swModel Is Nothing Then
        MsgBox "Open a SolidWorks part.", vbExclamation, _
               "Sheet Metal DXF - Native Batch"
        Exit Sub
    End If

    If swModel.GetType <> swDocPART Then
        MsgBox "This macro works only with SLDPRT files.", _
               vbExclamation, "Sheet Metal DXF - Native Batch"
        Exit Sub
    End If

    modelPath = swModel.GetPathName

    If modelPath = "" Then
        MsgBox "Save the part first.", vbExclamation, _
               "Sheet Metal DXF - Native Batch"
        Exit Sub
    End If

    Set swPart = swModel

    initialConfigurationName = _
        swModel.ConfigurationManager.ActiveConfiguration.Name

    currentStage = "Checking whether the model needs rebuilding"
    rebuildStart = Timer

    If SMART_REBUILD Then

        On Error Resume Next
        Err.Clear

        needsRebuild = CBool(swModel.Extension.NeedsRebuild2)
        rebuildCheckSucceeded = (Err.Number = 0)

        Err.Clear
        On Error GoTo MainError

    Else

        needsRebuild = True
        rebuildCheckSucceeded = True

    End If

    If Not rebuildCheckSucceeded Then needsRebuild = True

    If needsRebuild Then

        currentStage = "Rebuilding the active model"
        swModel.ForceRebuild3 False
        rebuildPerformed = True

    End If

    rebuildSeconds = ElapsedSeconds(rebuildStart, Timer)
    preparationStart = Timer

    modelFolder = Left$(modelPath, InStrRev(modelPath, "\") - 1)

    Set fso = GetSharedFileSystemObject()

    tempRoot = Environ$("TEMP") & _
               "\SW_DXF_BATCH_" & fso.GetTempName

    currentStage = "Preparing the temporary folder"
    CreateFolderIfNeeded tempRoot

    currentStage = "Collecting Flat-Pattern features"
    CollectFlatPatterns swModel, flatPatterns

    If flatPatterns.count = 0 Then

        MsgBox "No Flat-Pattern features found.", vbExclamation, _
               "Sheet Metal DXF - Native Batch"

        Exit Sub

    End If

    currentStage = "Creating a new DXF output folder"

    If Not CreateUniqueOutputFolder( _
        modelFolder, mainOutputFolder, outputFolderError) Then

        MsgBox "The output folder cannot be created." & _
               vbCrLf & vbCrLf & outputFolderError, _
               vbCritical, "Sheet Metal DXF - Native Batch"

        Exit Sub

    End If

    If SHOW_PROGRESS Then

        On Error Resume Next
        Err.Clear

        swApp.GetUserProgressBar swProgressBar

        If Not swProgressBar Is Nothing Then
            progressStarted = swProgressBar.Start( _
                0, 100, "Preparing grouped DXF export...")
        End If

        Err.Clear
        On Error GoTo MainError

    End If

    BatchProgress swProgressBar, progressStarted, 5, _
                  "Building Cut List cache..."

    currentStage = "Building Cut List cache"
    BuildCutListCache swModel, cachedBodies, cachedCutLists

    Set processed = CreateObject("Scripting.Dictionary")
    processed.CompareMode = vbTextCompare

    Set thicknessFolders = CreateObject("Scripting.Dictionary")
    thicknessFolders.CompareMode = vbTextCompare

    Set targetPaths = CreateObject("Scripting.Dictionary")
    targetPaths.CompareMode = vbTextCompare

    Set allocated = CreateObject("Scripting.Dictionary")
    allocated.CompareMode = vbTextCompare

    exportOptions = OPT_GEOMETRY

    If EXPORT_BEND_LINES Then
        exportOptions = exportOptions + OPT_BEND_LINES
    End If

    On Error Resume Next
    Err.Clear

    Set swView = swModel.ActiveView

    If Not swView Is Nothing Then
        swView.EnableGraphicsUpdate = False
        If Err.Number = 0 Then graphicsDisabled = True
    End If

    Err.Clear
    On Error GoTo MainError

    ' Preserve the original macro rule:
    ' export one representative per Cut List folder.

    For i = 1 To flatPatterns.count

        currentStage = "Preparing item " & CStr(i) & _
                       " of " & CStr(flatPatterns.count)

        Set flatFeat = flatPatterns.Item(i)

        BatchProgress swProgressBar, progressStarted, _
            5 + CLng(15# * CDbl(i - 1) / _
            CDbl(flatPatterns.count)), _
            "Preparing: " & flatFeat.Name

        Set swBody = GetBodyFromFlatPattern(flatFeat)

        If swBody Is Nothing Then

            failed = failed + 1

            If firstExportError = "" Then
                firstExportError = _
                    "Cannot get body from Flat-Pattern: " & _
                    flatFeat.Name
            End If

            GoTo NextFlat

        End If

        Set cutListFeat = FindCutListFeatureCached( _
            swBody, cachedBodies, cachedCutLists)

        If cutListFeat Is Nothing Then

            failed = failed + 1

            If firstExportError = "" Then
                firstExportError = _
                    "Cannot find Cut List for Flat-Pattern: " & _
                    flatFeat.Name
            End If

            GoTo NextFlat

        End If

        cutListKey = cutListFeat.Name

        If processed.Exists(cutListKey) Then
            skipped = skipped + 1
            GoTo NextFlat
        End If

        Set bodyFolder = cutListFeat.GetSpecificFeature2

        If bodyFolder Is Nothing Then

            failed = failed + 1

            If firstExportError = "" Then
                firstExportError = _
                    "Cannot get BodyFolder: " & cutListFeat.Name
            End If

            GoTo NextFlat

        End If

        qty = bodyFolder.GetBodyCount

        If qty <= 0 Then qty = 1

        thicknessMm = _
            GetSheetMetalThicknessMm(swBody, cutListFeat)

        If thicknessMm > 0 Then

            thicknessText = FormatNumberForFile(thicknessMm)

        Else

            thicknessText = "UNKNOWN"
            unknownThickness = unknownThickness + 1

        End If

        thicknessFolder = mainOutputFolder & "\" & _
                          thicknessText & " mm"

        If Not thicknessFolders.Exists(thicknessText) Then
            CreateFolderIfNeeded thicknessFolder
            thicknessFolders.Add thicknessText, True
        End If

        boxText = GetBodyBoxText(swBody)

        If boxText = "" Then boxText = "NO_BBOX"

        dxfName = CleanFileName(cutListFeat.Name) & _
                  "_" & CStr(qty) & "_" & boxText & ".dxf"

        finalPath = thicknessFolder & "\" & dxfName

        If targetPaths.Exists(finalPath) Then
            Err.Raise vbObjectError + 2400, , _
                "Output filename collision: " & finalPath
        End If

        targetPaths.Add finalPath, True

        queuedFeatures.Add flatFeat
        queuedPaths.Add finalPath
        queuedBodies.Add swBody.Name
        queuedCutLists.Add cutListFeat.Name

        processed.Add cutListKey, True

NextFlat:
    Next i

    preparationSeconds = ElapsedSeconds(preparationStart, Timer)

    If queuedFeatures.count > 0 Then

        ' Update progress BEFORE selecting the export features.
        ' No DoEvents between final selection and ExportToDWG2.

        BatchProgress swProgressBar, progressStarted, 25, _
                      "Native grouped DXF export..."

        currentStage = "Enabling graphics before native export"

        If Not swView Is Nothing Then
            swView.EnableGraphicsUpdate = True
            graphicsDisabled = False
        End If

        currentStage = "Selecting all queued Flat-Pattern features"
        selectStart = Timer

        SelectBatchFlatPatterns swModel, queuedFeatures

        selectionSeconds = ElapsedSeconds(selectStart, Timer)

        batchPath = tempRoot & "\NativeBatch.dxf"

        alignment = Empty
        views = Empty

        currentStage = "ONE NATIVE GROUP ExportToDWG2"

        VerifyExportContext swModel, initialConfigurationName, _
                            queuedFeatures.count

        batchStart = Timer
        exportAttempts = 1

        ' False = separate files for selected Flat-Patterns.
        ' Options: 1 = geometry; 4 = bend lines.
        ' Existing user mapping preferences are retained.

        batchReturned = swPart.ExportToDWG2( _
            batchPath, _
            modelPath, _
            swExportSheetMetal, _
            False, _
            alignment, _
            False, _
            False, _
            exportOptions, _
            views)

        exportSecondsTotal = ElapsedSeconds(batchStart, Timer)

        Set batchFiles = BatchDxfFiles(tempRoot)

        If Not batchReturned Then

            Err.Raise vbObjectError + 2405, , _
                "Grouped ExportToDWG2 returned FALSE." & _
                vbCrLf & "Selected: " & _
                CStr(queuedFeatures.count) & _
                vbCrLf & "Options: " & CStr(exportOptions) & _
                vbCrLf & "Model: " & modelPath & _
                vbCrLf & "Configuration: " & _
                initialConfigurationName & _
                vbCrLf & "Native files retained: " & tempRoot

        End If

        If batchFiles.count <> queuedFeatures.count Then

            Err.Raise vbObjectError + 2406, , _
                "Native file count differs." & vbCrLf & _
                "Expected: " & CStr(queuedFeatures.count) & _
                vbCrLf & "Actual: " & CStr(batchFiles.count) & _
                vbCrLf & "Native files retained: " & tempRoot

        End If

        Set manifest = fso.CreateTextFile( _
            mainOutputFolder & "\batch-files.csv", False, True)

        manifest.WriteLine _
            "Flat-Pattern;Body;Cut List;Native DXF;Final DXF"

        currentStage = "Verifying native names and copying DXF"

        For i = 1 To queuedFeatures.count

            Set batchFeature = queuedFeatures.Item(i)

            nativePath = FindNativeBatchDxf( _
                batchFiles, batchFeature.Name, "NativeBatch")

            If nativePath = "" Then

                Err.Raise vbObjectError + 2407, , _
                    "No unique native output for: " & _
                    batchFeature.Name & vbCrLf & _
                    "No order-based mapping used." & vbCrLf & _
                    "Native files retained: " & tempRoot

            End If

            If allocated.Exists(nativePath) Then
                Err.Raise vbObjectError + 2408, , _
                    "Native output reused twice: " & nativePath
            End If

            allocated.Add nativePath, True

            If FileLen(nativePath) <= 0 Then
                Err.Raise vbObjectError + 2409, , _
                    "Native DXF is empty: " & nativePath
            End If

            copyStart = Timer

            If Not CopyFileReplace( _
                nativePath, CStr(queuedPaths.Item(i))) Then

                Err.Raise vbObjectError + 2410, , _
                    "Cannot copy native DXF: " & _
                    CStr(queuedPaths.Item(i))

            End If

            copySecondsTotal = copySecondsTotal + _
                ElapsedSeconds(copyStart, Timer)

            exported = exported + 1

            manifest.WriteLine _
                CsvCell(batchFeature.Name) & ";" & _
                CsvCell(CStr(queuedBodies.Item(i))) & ";" & _
                CsvCell(CStr(queuedCutLists.Item(i))) & ";" & _
                CsvCell(nativePath) & ";" & _
                CsvCell(CStr(queuedPaths.Item(i)))

            BatchProgress swProgressBar, progressStarted, _
                70 + CLng(29# * CDbl(i) / _
                CDbl(queuedFeatures.count)), _
                "Verified/copied: " & batchFeature.Name

        Next i

        manifest.Close
        Set manifest = Nothing

    End If

    RestoreModelState swModel, initialConfigurationName, _
                      swView, graphicsDisabled

    EndMacroProgress swProgressBar, progressStarted, _
                     (failed = 0)

    totalSecondsValue = ElapsedSeconds(totalStart, Timer)

    If exported > 0 Then
        averageExportSeconds = _
            exportSecondsTotal / CDbl(exported)
    End If

    If failed = 0 Then
        resultText = "Export finished."
    Else
        resultText = "Export finished with preparation errors."
    End If

    resultText = resultText & vbCrLf & vbCrLf & _
        "DXF created: " & CStr(exported) & vbCrLf & _
        "Duplicates skipped: " & CStr(skipped) & vbCrLf & _
        "Preparation errors: " & CStr(failed) & vbCrLf & _
        "Native export calls: " & CStr(exportAttempts) & vbCrLf & _
        "Selected Flat-Patterns: " & CStr(queuedFeatures.count)

    If unknownThickness > 0 Then
        resultText = resultText & vbCrLf & _
            "Unknown thickness: " & CStr(unknownThickness)
    End If

    resultText = resultText & vbCrLf & vbCrLf & _
        "Total time: " & FormatElapsed(totalSecondsValue) & _
        vbCrLf & vbCrLf & "Performance:" & vbCrLf

    If rebuildPerformed Then
        resultText = resultText & _
            "Rebuild: " & FormatElapsedPrecise(rebuildSeconds)
    Else
        resultText = resultText & _
            "Rebuild: skipped (model was current)"
    End If

    resultText = resultText & vbCrLf & _
        "Preparation: " & _
        FormatElapsedPrecise(preparationSeconds) & vbCrLf & _
        "Selection: " & _
        FormatElapsedPrecise(selectionSeconds) & vbCrLf & _
        "ONE native grouped export: " & _
        FormatElapsedPrecise(exportSecondsTotal) & vbCrLf & _
        "File copy and verification: " & _
        FormatElapsedPrecise(copySecondsTotal)

    If exported > 0 Then
        resultText = resultText & vbCrLf & _
            "Native batch time / DXF: " & _
            FormatElapsedPrecise(averageExportSeconds)
    End If

    If firstExportError <> "" Then
        resultText = resultText & vbCrLf & vbCrLf & _
            "First preparation error:" & vbCrLf & _
            firstExportError
    End If

    resultText = resultText & vbCrLf & vbCrLf & _
        "Final folder:" & vbCrLf & mainOutputFolder & _
        vbCrLf & vbCrLf & _
        "Native work folder:" & vbCrLf & tempRoot

    Set reportFile = fso.CreateTextFile( _
        mainOutputFolder & "\batch-report.txt", False, True)

    reportFile.Write resultText
    reportFile.Close
    Set reportFile = Nothing

    MsgBox resultText, vbInformation, _
           "Sheet Metal DXF - Native Batch"

    Exit Sub

MainError:

    Dim mainErr As String
    Dim mainErrNumber As Long

    mainErrNumber = Err.Number
    mainErr = Err.Description

    If mainErr = "" Then mainErr = "Unknown VBA error."

    On Error Resume Next

    If Not manifest Is Nothing Then manifest.Close
    If Not reportFile Is Nothing Then reportFile.Close

    RestoreModelState swModel, initialConfigurationName, _
                      swView, graphicsDisabled

    EndMacroProgress swProgressBar, progressStarted, False

    On Error GoTo 0

    MsgBox _
        "Macro error " & CStr(mainErrNumber) & ":" & _
        vbCrLf & mainErr & vbCrLf & vbCrLf & _
        "Stage:" & vbCrLf & currentStage & _
        vbCrLf & vbCrLf & _
        "Native work folder:" & vbCrLf & tempRoot, _
        vbCritical, "Sheet Metal DXF - Native Batch"

End Sub

Sub SelectBatchFlatPatterns( _
    ByVal model As SldWorks.ModelDoc2, _
    ByVal features As Collection)

    Dim part As SldWorks.PartDoc
    Dim active As SldWorks.ModelDoc2
    Dim original As SldWorks.Feature
    Dim fresh As SldWorks.Feature

    Dim objects() As Object
    Dim selected As Object
    Dim manager As SldWorks.SelectionMgr
    Dim selectData As SldWorks.selectData

    Dim i As Long
    Dim j As Long
    Dim count As Long
    Dim hits As Long

    If features.count = 0 Then
        Err.Raise vbObjectError + 2420, , "Empty batch"
    End If

    Set active = swApp.ActiveDoc

    If active Is Nothing Then
        Err.Raise vbObjectError + 2421, , "No active document"
    End If

    If swApp.IsSame(active, model) <> 1 Then
        Err.Raise vbObjectError + 2422, , _
            "Active document changed before selection"
    End If

    Set part = model
    ReDim objects(0 To features.count - 1)

    For i = 1 To features.count

        Set original = features.Item(i)
        Set fresh = part.FeatureByName(original.Name)

        If fresh Is Nothing Then
            Err.Raise vbObjectError + 2423, , _
                "Feature unavailable: " & original.Name
        End If

        If LCase$(fresh.GetTypeName2) <> "flatpattern" Then
            Err.Raise vbObjectError + 2424, , _
                "Not a Flat-Pattern: " & original.Name
        End If

        If swApp.IsSame(fresh, original) <> 1 Then
            Err.Raise vbObjectError + 2425, , _
                "Feature identity differs: " & original.Name
        End If

        Set objects(i - 1) = fresh

    Next i

    model.ClearSelection2 True

    Set manager = model.SelectionManager
    Set selectData = manager.CreateSelectData

    selectData.Mark = -1

    count = model.Extension.MultiSelect2( _
        objects, False, selectData)

    If count <> features.count Or _
       manager.GetSelectedObjectCount2(-1) <> features.count Then

        Err.Raise vbObjectError + 2426, , _
            "Native batch selection incomplete: " & CStr(count)

    End If

    For i = 1 To manager.GetSelectedObjectCount2(-1)

        Set selected = manager.GetSelectedObject6(i, -1)

        If selected Is Nothing Then
            Err.Raise vbObjectError + 2427, , _
                "Selected object unavailable"
        End If

        hits = 0

        For j = 0 To UBound(objects)
            If swApp.IsSame(selected, objects(j)) = 1 Then
                hits = hits + 1
            End If
        Next j

        If hits <> 1 Then
            Err.Raise vbObjectError + 2428, , _
                "Unexpected or duplicate feature in selection"
        End If

    Next i

    For j = 0 To UBound(objects)

        hits = 0

        For i = 1 To manager.GetSelectedObjectCount2(-1)

            Set selected = manager.GetSelectedObject6(i, -1)

            If swApp.IsSame(selected, objects(j)) = 1 Then
                hits = hits + 1
            End If

        Next i

        If hits <> 1 Then
            Err.Raise vbObjectError + 2429, , _
                "Expected feature missing or duplicated"
        End If

    Next j

End Sub

Sub VerifyExportContext( _
    ByVal model As SldWorks.ModelDoc2, _
    ByVal configurationName As String, _
    ByVal expectedSelectionCount As Long)

    Dim active As SldWorks.ModelDoc2
    Dim manager As SldWorks.SelectionMgr

    Set active = swApp.ActiveDoc

    If active Is Nothing Then
        Err.Raise vbObjectError + 2430, , _
            "No active document immediately before export"
    End If

    If swApp.IsSame(active, model) <> 1 Then
        Err.Raise vbObjectError + 2431, , _
            "Active document changed immediately before export"
    End If

    If StrComp( _
        model.ConfigurationManager.ActiveConfiguration.Name, _
        configurationName, vbBinaryCompare) <> 0 Then

        Err.Raise vbObjectError + 2432, , _
            "Configuration changed immediately before export"

    End If

    Set manager = model.SelectionManager

    If manager.GetSelectedObjectCount2(-1) <> _
       expectedSelectionCount Then

        Err.Raise vbObjectError + 2433, , _
            "Selection changed immediately before export"

    End If

End Sub

Sub BuildCutListCache( _
    ByVal swModel As SldWorks.ModelDoc2, _
    ByRef cachedBodies As Collection, _
    ByRef cachedCutLists As Collection)

    Dim feat As SldWorks.Feature
    Set feat = swModel.FirstFeature

    Do While Not feat Is Nothing

        CollectCutListCacheBranch _
            feat, cachedBodies, cachedCutLists

        Set feat = feat.GetNextFeature

    Loop

End Sub

Sub CollectCutListCacheBranch( _
    ByVal feat As SldWorks.Feature, _
    ByRef cachedBodies As Collection, _
    ByRef cachedCutLists As Collection)

    On Error Resume Next

    If LCase$(feat.GetTypeName2) = "cutlistfolder" Then

        Dim bodyFolder As SldWorks.bodyFolder
        Set bodyFolder = feat.GetSpecificFeature2

        If Not bodyFolder Is Nothing Then

            Dim bodies As Variant
            bodies = bodyFolder.GetBodies

            If Not IsEmpty(bodies) Then

                Dim i As Long

                For i = LBound(bodies) To UBound(bodies)
                    cachedBodies.Add bodies(i)
                    cachedCutLists.Add feat
                Next i

            End If

        End If

    End If

    Err.Clear
    On Error GoTo 0

    Dim subFeat As SldWorks.Feature
    Set subFeat = feat.GetFirstSubFeature

    Do While Not subFeat Is Nothing

        CollectCutListCacheBranch _
            subFeat, cachedBodies, cachedCutLists

        Set subFeat = subFeat.GetNextSubFeature

    Loop

End Sub

Function FindCutListFeatureCached( _
    ByVal targetBody As SldWorks.Body2, _
    ByVal cachedBodies As Collection, _
    ByVal cachedCutLists As Collection) As SldWorks.Feature

    On Error GoTo ErrorHandler

    Dim i As Long
    Dim testBody As SldWorks.Body2

    For i = 1 To cachedBodies.count

        Set testBody = cachedBodies.Item(i)

        If swApp.IsSame(testBody, targetBody) = 1 Then
            Set FindCutListFeatureCached = cachedCutLists.Item(i)
            Exit Function
        End If

    Next i

    Exit Function

ErrorHandler:

    Set FindCutListFeatureCached = Nothing

End Function

Sub CollectFlatPatterns( _
    ByVal swModel As SldWorks.ModelDoc2, _
    ByRef result As Collection)

    Dim feat As SldWorks.Feature
    Set feat = swModel.FirstFeature

    Do While Not feat Is Nothing

        CollectFlatPatternBranch feat, result
        Set feat = feat.GetNextFeature

    Loop

End Sub

Sub CollectFlatPatternBranch( _
    ByVal feat As SldWorks.Feature, _
    ByRef result As Collection)

    If LCase$(feat.GetTypeName2) = "flatpattern" Then
        result.Add feat
    End If

    Dim subFeat As SldWorks.Feature
    Set subFeat = feat.GetFirstSubFeature

    Do While Not subFeat Is Nothing

        CollectFlatPatternBranch subFeat, result
        Set subFeat = subFeat.GetNextSubFeature

    Loop

End Sub

Function GetBodyFromFlatPattern( _
    ByVal flatFeat As SldWorks.Feature) As SldWorks.Body2

    On Error GoTo ErrorHandler

    Dim flatData As SldWorks.FlatPatternFeatureData
    Set flatData = flatFeat.GetDefinition

    If flatData Is Nothing Then Exit Function

    Dim fixedEntity As Object
    Set fixedEntity = flatData.FixedFace2

    If fixedEntity Is Nothing Then Exit Function

    Dim swBody As SldWorks.Body2

    On Error Resume Next
    Set swBody = fixedEntity.GetBody
    On Error GoTo ErrorHandler

    If Not swBody Is Nothing Then
        Set GetBodyFromFlatPattern = swBody
    End If

    Exit Function

ErrorHandler:

    Set GetBodyFromFlatPattern = Nothing

End Function

Function GetSheetMetalThicknessMm( _
    ByVal swBody As SldWorks.Body2, _
    ByVal cutListFeat As SldWorks.Feature) As Double

    Dim value As Double

    value = GetThicknessFromBodyFeatures(swBody)

    If value > 0 Then
        GetSheetMetalThicknessMm = value
        Exit Function
    End If

    value = GetThicknessFromCutList(cutListFeat)

    If value > 0 Then
        GetSheetMetalThicknessMm = value
        Exit Function
    End If

    GetSheetMetalThicknessMm = 0

End Function

Function GetThicknessFromBodyFeatures( _
    ByVal swBody As SldWorks.Body2) As Double

    On Error GoTo ErrorHandler

    Dim features As Variant
    features = swBody.GetFeatures

    If IsEmpty(features) Then Exit Function

    Dim i As Long
    Dim feat As SldWorks.Feature
    Dim typeName As String
    Dim featData As Object
    Dim thicknessMeters As Double

    For i = LBound(features) To UBound(features)

        Set feat = features(i)

        If Not feat Is Nothing Then

            typeName = LCase$(Trim$(feat.GetTypeName2))

            If typeName = "smbaseflange" Or _
               typeName = "sheetmetal" Then

                Set featData = Nothing

                On Error Resume Next
                Set featData = feat.GetDefinition

                If Not featData Is Nothing Then

                    thicknessMeters = 0
                    Err.Clear

                    thicknessMeters = CDbl(featData.Thickness)

                    If Err.Number = 0 And _
                       thicknessMeters > 0 Then

                        GetThicknessFromBodyFeatures = _
                            thicknessMeters * 1000#

                        On Error GoTo 0
                        Exit Function

                    End If

                End If

                Err.Clear
                On Error GoTo ErrorHandler

            End If

        End If

    Next i

    Exit Function

ErrorHandler:

    GetThicknessFromBodyFeatures = 0

End Function

Function GetThicknessFromCutList( _
    ByVal cutListFeat As SldWorks.Feature) As Double

    On Error GoTo ErrorHandler

    Dim propMgr As SldWorks.CustomPropertyManager
    Set propMgr = cutListFeat.CustomPropertyManager

    If propMgr Is Nothing Then Exit Function

    Dim propertyNames As Variant

    propertyNames = Array( _
        "Sheet Metal Thickness", _
        "Thickness")

    Dim i As Long
    Dim rawValue As String
    Dim resolvedValue As String
    Dim value As Double

    For i = LBound(propertyNames) To UBound(propertyNames)

        rawValue = ""
        resolvedValue = ""

        propMgr.Get2 CStr(propertyNames(i)), _
                     rawValue, resolvedValue

        value = ParseNumericValue(resolvedValue)

        If value <= 0 Then
            value = ParseNumericValue(rawValue)
        End If

        If value > 0 Then
            GetThicknessFromCutList = value
            Exit Function
        End If

    Next i

    Exit Function

ErrorHandler:

    GetThicknessFromCutList = 0

End Function

Function GetBodyBoxText( _
    ByVal swBody As SldWorks.Body2) As String

    On Error GoTo ErrorHandler

    Dim boxValues As Variant
    boxValues = swBody.GetBodyBox

    If IsEmpty(boxValues) Then Exit Function

    Dim sizeX As Double
    Dim sizeY As Double
    Dim sizeZ As Double

    sizeX = Abs(CDbl(boxValues(3)) - _
                CDbl(boxValues(0))) * 1000#

    sizeY = Abs(CDbl(boxValues(4)) - _
                CDbl(boxValues(1))) * 1000#

    sizeZ = Abs(CDbl(boxValues(5)) - _
                CDbl(boxValues(2))) * 1000#

    sizeX = RoundDimension(sizeX)
    sizeY = RoundDimension(sizeY)
    sizeZ = RoundDimension(sizeZ)

    GetBodyBoxText = _
        FormatNumberForFile(sizeX) & "x" & _
        FormatNumberForFile(sizeY) & "x" & _
        FormatNumberForFile(sizeZ)

    Exit Function

ErrorHandler:

    GetBodyBoxText = ""

End Function

Function RoundDimension(ByVal value As Double) As Double

    Dim nearestInteger As Double
    nearestInteger = Round(value, 0)

    If Abs(value - nearestInteger) < 0.001 Then
        RoundDimension = nearestInteger
    Else
        RoundDimension = Round(value, 3)
    End If

End Function

Function FormatNumberForFile( _
    ByVal value As Double) As String

    Dim txt As String

    txt = Format$(value, "0.###")
    txt = Replace$(txt, ",", ".")

    FormatNumberForFile = txt

End Function

Function ParseNumericValue(ByVal txt As String) As Double

    On Error GoTo ErrorHandler

    txt = Trim$(txt)

    If txt = "" Then Exit Function

    txt = Replace$(txt, ",", ".")

    Dim numberText As String
    Dim started As Boolean
    Dim i As Long
    Dim ch As String

    For i = 1 To Len(txt)

        ch = Mid$(txt, i, 1)

        If ch >= "0" And ch <= "9" Then

            numberText = numberText & ch
            started = True

        ElseIf ch = "." Then

            If started Then

                If InStr(1, numberText, ".") = 0 Then
                    numberText = numberText & "."
                End If

            End If

        Else

            If started Then Exit For

        End If

    Next i

    If numberText <> "" Then
        ParseNumericValue = Val(numberText)
    End If

    Exit Function

ErrorHandler:

    ParseNumericValue = 0

End Function

Function CleanFileName(ByVal txt As String) As String

    Dim invalidChars As Variant

    invalidChars = Array( _
        "\", "/", ":", "*", "?", """", "<", ">", "|")

    Dim i As Long

    For i = LBound(invalidChars) To UBound(invalidChars)
        txt = Replace$(txt, invalidChars(i), "_")
    Next i

    Do While InStr(1, txt, "__", vbBinaryCompare) > 0
        txt = Replace$(txt, "__", "_")
    Loop

    Do While Len(txt) > 0 And Right$(txt, 1) = "_"
        txt = Left$(txt, Len(txt) - 1)
    Loop

    CleanFileName = Trim$(txt)

End Function

Function GetSharedFileSystemObject() As Object

    If gFileSystemObject Is Nothing Then

        Set gFileSystemObject = _
            CreateObject("Scripting.FileSystemObject")

    End If

    Set GetSharedFileSystemObject = gFileSystemObject

End Function

Function CreateUniqueOutputFolder( _
    ByVal modelFolder As String, _
    ByRef createdFolderPath As String, _
    ByRef errorText As String) As Boolean

    On Error GoTo ErrorHandler

    Dim fso As Object
    Set fso = GetSharedFileSystemObject()

    If Not fso.FolderExists(modelFolder) Then
        Err.Raise vbObjectError + 2101, , _
            "The folder containing the active SLDPRT does not exist."
    End If

    Dim folderIndex As Long
    Dim folderName As String
    Dim candidatePath As String

    folderIndex = 0

    Do

        If folderIndex = 0 Then
            folderName = OUTPUT_FOLDER
        Else
            folderName = OUTPUT_FOLDER & CStr(folderIndex)
        End If

        candidatePath = modelFolder & "\" & folderName

        If Not fso.FolderExists(candidatePath) And _
           Not fso.FileExists(candidatePath) Then

            fso.CreateFolder candidatePath

            If Not fso.FolderExists(candidatePath) Then
                Err.Raise vbObjectError + 2102, , _
                    "Windows did not create the new output folder."
            End If

            createdFolderPath = candidatePath
            CreateUniqueOutputFolder = True

            Exit Function

        End If

        folderIndex = folderIndex + 1

        If folderIndex > 999999 Then
            Err.Raise vbObjectError + 2103, , _
                "No available DXF folder name was found."
        End If

    Loop

ErrorHandler:

    errorText = Err.Description
    createdFolderPath = ""
    CreateUniqueOutputFolder = False

End Function

Function CopyFileReplace( _
    ByVal sourcePath As String, _
    ByVal destinationPath As String) As Boolean

    On Error GoTo ErrorHandler

    Dim fso As Object
    Set fso = GetSharedFileSystemObject()

    fso.CopyFile sourcePath, destinationPath, True

    If Not fso.FileExists(destinationPath) Then Exit Function

    CopyFileReplace = _
        (fso.GetFile(sourcePath).Size = _
         fso.GetFile(destinationPath).Size)

    Exit Function

ErrorHandler:

    CopyFileReplace = False

End Function

Sub CreateFolderIfNeeded(ByVal folderPath As String)

    Dim fso As Object
    Set fso = GetSharedFileSystemObject()

    If Not fso.FolderExists(folderPath) Then
        fso.CreateFolder folderPath
    End If

End Sub

Function FindNativeBatchDxf( _
    ByVal paths As Collection, _
    ByVal featureName As String, _
    ByVal baseName As String) As String

    Dim p As Variant
    Dim stem As String
    Dim expected As String
    Dim cleanExpected As String
    Dim hits As Long

    expected = featureName & " - " & baseName

    cleanExpected = CleanFileName(featureName) & _
                    " - " & baseName

    For Each p In paths

        stem = GetSharedFileSystemObject().GetBaseName(CStr(p))

        If StrComp(stem, expected, vbTextCompare) = 0 Or _
           StrComp(stem, cleanExpected, vbTextCompare) = 0 Then

            hits = hits + 1
            FindNativeBatchDxf = CStr(p)

        End If

    Next p

    If hits <> 1 Then FindNativeBatchDxf = ""

End Function

Function BatchDxfFiles(ByVal folder As String) As Collection

    Dim result As New Collection
    Dim f As Object
    Dim fso As Object

    Set fso = GetSharedFileSystemObject()

    For Each f In fso.GetFolder(folder).Files

        If LCase$(fso.GetExtensionName(f.Name)) = "dxf" Then
            result.Add CStr(f.Path)
        End If

    Next f

    Set BatchDxfFiles = result

End Function

Function CsvCell(ByVal value As String) As String

    CsvCell = Chr$(34) & _
              Replace(value, Chr$(34), _
              Chr$(34) & Chr$(34)) & Chr$(34)

End Function

Sub BatchProgress( _
    ByVal bar As SldWorks.UserProgressBar, _
    ByVal started As Boolean, _
    ByVal value As Long, _
    ByVal title As String)

    If Not started Then Exit Sub
    If bar Is Nothing Then Exit Sub

    On Error Resume Next

    bar.UpdateProgress value
    bar.UpdateTitle title

    DoEvents

    On Error GoTo 0

End Sub

Sub EndMacroProgress( _
    ByRef swProgressBar As SldWorks.UserProgressBar, _
    ByRef progressStarted As Boolean, _
    ByVal completedSuccessfully As Boolean)

    On Error Resume Next

    If progressStarted Then

        If Not swProgressBar Is Nothing Then

            If completedSuccessfully Then
                swProgressBar.UpdateTitle "DXF export completed"
                swProgressBar.UpdateProgress 100
            Else
                swProgressBar.UpdateTitle "DXF export stopped"
            End If

            DoEvents
            swProgressBar.End

        End If

    End If

    progressStarted = False
    Set swProgressBar = Nothing

    On Error GoTo 0

End Sub

Sub RestoreModelState( _
    ByVal swModel As SldWorks.ModelDoc2, _
    ByVal initialConfigurationName As String, _
    ByVal swView As Object, _
    ByVal graphicsDisabled As Boolean)

    On Error Resume Next

    If Not swModel Is Nothing Then

        swModel.ClearSelection2 True

        If initialConfigurationName <> "" Then

            Dim currentConfigurationName As String

            currentConfigurationName = _
                swModel.ConfigurationManager.ActiveConfiguration.Name

            If StrComp( _
                currentConfigurationName, _
                initialConfigurationName, _
                vbBinaryCompare) <> 0 Then

                swModel.ShowConfiguration2 _
                    initialConfigurationName

            End If

        End If

    End If

    If graphicsDisabled Then

        If Not swView Is Nothing Then
            swView.EnableGraphicsUpdate = True
        End If

    End If

    If Not swModel Is Nothing Then swModel.GraphicsRedraw2

    On Error GoTo 0

End Sub

Function ElapsedSeconds( _
    ByVal startValue As Double, _
    ByVal endValue As Double) As Double

    If endValue >= startValue Then
        ElapsedSeconds = endValue - startValue
    Else
        ElapsedSeconds = (86400# - startValue) + endValue
    End If

End Function

Function FormatElapsed(ByVal secondsValue As Double) As String

    Dim totalSeconds As Long
    Dim hoursValue As Long
    Dim minutesValue As Long
    Dim secondsPart As Long

    totalSeconds = CLng(secondsValue)

    hoursValue = totalSeconds \ 3600
    minutesValue = (totalSeconds Mod 3600) \ 60
    secondsPart = totalSeconds Mod 60

    If hoursValue > 0 Then

        FormatElapsed = _
            Format$(hoursValue, "00") & ":" & _
            Format$(minutesValue, "00") & ":" & _
            Format$(secondsPart, "00")

    Else

        FormatElapsed = _
            Format$(minutesValue, "00") & ":" & _
            Format$(secondsPart, "00")

    End If

End Function

Function FormatElapsedPrecise( _
    ByVal secondsValue As Double) As String

    If secondsValue < 60# Then

        FormatElapsedPrecise = _
            Format$(secondsValue, "0.0") & " sec"

    Else

        FormatElapsedPrecise = FormatElapsed(secondsValue)

    End If

End Function

