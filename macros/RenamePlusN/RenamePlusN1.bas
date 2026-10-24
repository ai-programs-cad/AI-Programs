Attribute VB_Name = "RenamePlusN1"
Option Explicit

Private swApp As SldWorks.SldWorks
Private swModel As SldWorks.ModelDoc2
Private tempPrefix As String
Private movedOutside As Long
Private progressBar As SldWorks.UserProgressBar
Private progressValue As Long
Private progressStarted As Boolean
Private currentPass As Long

Private Const SW_DOC_PART As Long = 1
Private Const CUSTOM_INFO_TEXT As Long = 30
Private Const CUSTOM_PROP_DELETE_AND_ADD As Long = 1
Private Const MAX_PASSES As Long = 3

Public Sub main()
    Dim started As Double, elapsed As Double
    Dim pass As Long, folders As Collection, active As Collection
    Dim bodyLists As Collection, bodies As Long, bad As Long
    Dim details As String, passDetails As String
    Dim propBad As Long, includeBad As Long, verified As Boolean
    Dim message As String

    On Error GoTo UnexpectedError
    started = Timer
    movedOutside = 0

    Set swApp = Application.SldWorks
    Set swModel = swApp.ActiveDoc

    If swModel Is Nothing Then
        MsgBox "No active document.", vbExclamation, "RenamePlusN"
        Exit Sub
    End If

    If swModel.GetType <> SW_DOC_PART Then
        MsgBox "Open a part document.", vbExclamation, "RenamePlusN"
        Exit Sub
    End If

    BeginProgress
    ShowProgress 1, "Rebuilding model"
    swModel.ForceRebuild3 False
    ShowProgress 5, "Scanning Cut List"

    For pass = 1 To MAX_PASSES
        currentPass = pass

        Set folders = New Collection
        Set active = New Collection
        Set bodyLists = New Collection
        ScanTree folders, active, bodyLists, bodies

        If folders.Count = 0 Or active.Count = 0 Then
            If pass > 1 Then
                details = ""
                RemoveTemporaryBodyNames details
            End If

            MsgBox "No Cut List folders containing bodies were found.", _
                   vbExclamation, "RenamePlusN"

            EndProgress
            Exit Sub
        End If

        Randomize
        tempPrefix = "__RCL_" & _
                     Format$(Now, "yymmddhhnnss") & "_" & _
                     CStr(CLng(Timer * 1000#)) & "_" & _
                     CStr(Int(Rnd * 100000#)) & "_"

        passDetails = ""
        RenamePass folders, active, bodyLists, passDetails

        ShowProgress _
            5 + (pass - 1) * 27 + 25, _
            "Rebuilding: pass " & CStr(pass)

        swModel.ForceRebuild3 False

        Set folders = New Collection
        Set active = New Collection
        Set bodyLists = New Collection
        ScanTree folders, active, bodyLists, bodies

        details = ""
        bad = CheckNames(folders, active, bodyLists, details)

        ShowProgress _
            5 + pass * 27, _
            "Checking names: pass " & CStr(pass)

        If bad = 0 Then
            verified = True
            Exit For
        End If
    Next pass

    If Not verified Then
        DirectFinalNames folders, active, bodyLists, details

        details = ""
        bad = CheckNames(folders, active, bodyLists, details)

        If bad > 0 Then
            RemoveTemporaryBodyNames details
            swModel.FeatureManager.UpdateFeatureTree
            swModel.GraphicsRedraw2

            MsgBox "Some names could not be set (" & _
                   CStr(bad) & ")." & vbCrLf & _
                   "The model was NOT saved by this macro." & _
                   vbCrLf & vbCrLf & details, _
                   vbExclamation, "RenamePlusN"
        Else
            MsgBox "Names are correct now, but the Cut List changed after " & _
                   "each rebuild. Check the model before saving; another " & _
                   "rebuild may change the names again. Property N was " & _
                   "not updated.", vbExclamation, "RenamePlusN"
        End If

        EndProgress
        Exit Sub
    End If

    If pass > MAX_PASSES Then pass = MAX_PASSES

    ShowProgress 90, "Updating Cut List and property N"

    IncludeActive active, includeBad, details
    UpdatePropertyN active, propBad, details

    swModel.FeatureManager.UpdateFeatureTree
    swModel.GraphicsRedraw2

    elapsed = Timer - started
    If elapsed < 0 Then elapsed = elapsed + 86400#

    ShowProgress 100, "Completed"
    EndProgress

    If propBad + includeBad = 0 Then
        MsgBox "Renaming completed." & vbCrLf & _
               "Cut List folders: " & CStr(active.Count) & vbCrLf & _
               "Bodies: " & CStr(bodies) & vbCrLf & _
               "Unused folders: " & _
               CStr(folders.Count - active.Count) & vbCrLf & _
               "Other bodies with reserved names moved: " & _
               CStr(movedOutside) & vbCrLf & _
               "Passes: " & CStr(pass) & vbCrLf & _
               "Time: " & Format$(elapsed, "0.00") & " sec", _
               vbInformation, "RenamePlusN"
    Else
        MsgBox "Names were set, but some additional operations failed." & _
               vbCrLf & _
               "N property errors: " & CStr(propBad) & vbCrLf & _
               "Cut List inclusion errors: " & _
               CStr(includeBad) & vbCrLf & vbCrLf & _
               details, vbExclamation, "RenamePlusN"
    End If

    Exit Sub

UnexpectedError:
    message = Err.Description

    On Error Resume Next

    If Not swModel Is Nothing Then
        Set folders = New Collection
        Set active = New Collection
        Set bodyLists = New Collection

        ScanTree folders, active, bodyLists, bodies

        details = ""
        DirectFinalNames folders, active, bodyLists, details
        RemoveTemporaryBodyNames details

        swModel.FeatureManager.UpdateFeatureTree
        swModel.GraphicsRedraw2
    End If

    On Error GoTo 0
    EndProgress

    MsgBox "Macro error: " & message & vbCrLf & _
           "Check the names in the Cut List before saving.", _
           vbCritical, "RenamePlusN"
End Sub

Private Sub ScanTree(ByRef folders As Collection, _
                     ByRef active As Collection, _
                     ByRef bodyLists As Collection, _
                     ByRef totalBodies As Long)

    Dim feat As SldWorks.Feature

    totalBodies = 0
    Set feat = swModel.FirstFeature

    Do While Not feat Is Nothing
        ScanOne feat, folders, active, bodyLists, totalBodies
        Set feat = feat.GetNextFeature
    Loop
End Sub

Private Sub ScanOne(ByVal feat As SldWorks.Feature, _
                    ByRef folders As Collection, _
                    ByRef active As Collection, _
                    ByRef bodyLists As Collection, _
                    ByRef totalBodies As Long)

    Dim bf As SldWorks.BodyFolder
    Dim child As SldWorks.Feature
    Dim nextChild As SldWorks.Feature
    Dim items As Collection
    Dim v As Variant
    Dim b As SldWorks.Body2
    Dim i As Long

    If feat.GetTypeName2 = "CutListFolder" Then
        If Not HasFeature(folders, feat) Then
            folders.Add feat
            Set bf = feat.GetSpecificFeature2

            If Not bf Is Nothing Then
                If bf.GetBodyCount > 0 Then
                    Set items = New Collection
                    v = bf.GetBodies

                    If IsArray(v) Then
                        For i = LBound(v) To UBound(v)
                            Set b = v(i)
                            If Not b Is Nothing Then items.Add b
                        Next i
                    End If

                    If items.Count > 0 Then
                        active.Add feat
                        bodyLists.Add items
                        totalBodies = totalBodies + items.Count
                    End If
                End If
            End If
        End If
    End If

    Set child = feat.GetFirstSubFeature

    Do While Not child Is Nothing
        Set nextChild = child.GetNextSubFeature
        ScanOne child, folders, active, bodyLists, totalBodies
        Set child = nextChild
    Loop
End Sub

Private Function HasFeature(ByVal items As Collection, _
                            ByVal feat As SldWorks.Feature) As Boolean

    Dim item As SldWorks.Feature

    For Each item In items
        If item Is feat Then
            HasFeature = True
            Exit Function
        End If
    Next item
End Function

Private Sub RenamePass(ByVal folders As Collection, _
                       ByVal active As Collection, _
                       ByVal bodyLists As Collection, _
                       ByRef details As String)

    Dim i As Long
    Dim j As Long
    Dim globalIndex As Long
    Dim item As SldWorks.Feature
    Dim b As SldWorks.Body2
    Dim list As Collection
    Dim base As Long
    Dim total As Long

    base = 5 + (currentPass - 1) * 27

    For i = 1 To bodyLists.Count
        Set list = bodyLists(i)
        total = total + list.Count
    Next i

    For i = 1 To folders.Count
        Set item = folders(i)

        SetFeatureName _
            item, tempPrefix & "F" & CStr(i), details

        ShowProgress _
            base + 3 * i \ folders.Count, _
            "Temporary folders " & _
            CStr(i) & "/" & CStr(folders.Count)
    Next i

    For i = 1 To bodyLists.Count
        Set list = bodyLists(i)

        For j = 1 To list.Count
            Set b = list(j)
            globalIndex = globalIndex + 1

            SetBodyName _
                b, tempPrefix & "B" & CStr(globalIndex), details

            If total > 0 Then
                ShowProgress _
                    base + 3 + 10 * globalIndex \ total, _
                    "Temporary bodies " & _
                    CStr(globalIndex) & "/" & CStr(total)
            End If
        Next j
    Next i

    VacateExternalBodyNames bodyLists, details
    ShowProgress base + 16, "Resolving name conflicts"

    DirectFinalNames folders, active, bodyLists, details
    ShowProgress _
        base + 24, _
        "Final names: pass " & CStr(currentPass)
End Sub

Private Sub VacateExternalBodyNames( _
    ByVal bodyLists As Collection, _
    ByRef details As String)

    Dim part As SldWorks.PartDoc
    Dim v As Variant
    Dim i As Long
    Dim j As Long
    Dim k As Long
    Dim n As Long
    Dim candidate As SldWorks.Body2
    Dim list As Collection
    Dim required As String
    Dim safeName As String

    Set part = swModel
    v = part.GetBodies2(-1, False)

    If Not IsArray(v) Then Exit Sub

    For k = LBound(v) To UBound(v)
        Set candidate = v(k)

        If Not BodyIsInLists(candidate, bodyLists) Then
            For i = 1 To bodyLists.Count
                Set list = bodyLists(i)

                For j = 1 To list.Count
                    required = CStr(i) & "-" & CStr(j)

                    If StrComp(candidate.Name, required, _
                               vbBinaryCompare) = 0 Then

                        n = n + 1
                        safeName = "UNASSIGNED_BODY_" & _
                                   Format$(n, "000")

                        Do While swModel.FeatureManager.IsNameUsed( _
                                    0, safeName)

                            n = n + 1
                            safeName = "UNASSIGNED_BODY_" & _
                                       Format$(n, "000")
                        Loop

                        SetBodyName candidate, safeName, details

                        If StrComp(candidate.Name, safeName, _
                                   vbBinaryCompare) = 0 Then

                            movedOutside = movedOutside + 1

                            AddDetail details, _
                                "Unlisted body " & required & _
                                " renamed " & safeName
                        End If

                        Exit For
                    End If
                Next j
            Next i
        End If
    Next k
End Sub

Private Function BodyIsInLists( _
    ByVal testBody As SldWorks.Body2, _
    ByVal bodyLists As Collection) As Boolean

    Dim list As Collection
    Dim body As SldWorks.Body2

    For Each list In bodyLists
        For Each body In list
            If body Is testBody Then
                BodyIsInLists = True
                Exit Function
            End If
        Next body
    Next list
End Function

Private Sub DirectFinalNames(ByVal folders As Collection, _
                             ByVal active As Collection, _
                             ByVal bodyLists As Collection, _
                             ByRef details As String)

    Dim i As Long
    Dim j As Long
    Dim unusedIndex As Long
    Dim item As SldWorks.Feature
    Dim b As SldWorks.Body2
    Dim list As Collection
    Dim base As Long
    Dim total As Long
    Dim done As Long

    base = 5 + (currentPass - 1) * 27

    For i = 1 To bodyLists.Count
        Set list = bodyLists(i)
        total = total + list.Count
    Next i

    For i = 1 To active.Count
        Set item = active(i)
        SetFeatureName item, CStr(i), details
    Next i

    For i = 1 To folders.Count
        Set item = folders(i)

        If Not HasFeature(active, item) Then
            unusedIndex = unusedIndex + 1

            SetFeatureName item, _
                "UNUSED_CUTLIST_" & _
                Format$(unusedIndex, "000"), details
        End If
    Next i

    For i = 1 To bodyLists.Count
        Set list = bodyLists(i)

        For j = 1 To list.Count
            Set b = list(j)

            SetBodyName _
                b, CStr(i) & "-" & CStr(j), details

            done = done + 1

            If total > 0 Then
                ShowProgress _
                    base + 17 + 7 * done \ total, _
                    "Final bodies " & _
                    CStr(done) & "/" & CStr(total)
            End If
        Next j
    Next i
End Sub

Private Function CheckNames(ByVal folders As Collection, _
                            ByVal active As Collection, _
                            ByVal bodyLists As Collection, _
                            ByRef details As String) As Long

    Dim i As Long
    Dim j As Long
    Dim unusedIndex As Long
    Dim item As SldWorks.Feature
    Dim b As SldWorks.Body2
    Dim list As Collection
    Dim expected As String

    If folders.Count = 0 Or active.Count = 0 Then
        CheckNames = CheckNames + 1
        AddDetail details, "Cut List is empty after rebuilding."
        Exit Function
    End If

    For i = 1 To active.Count
        Set item = active(i)
        expected = CStr(i)

        If StrComp(item.Name, expected, _
                   vbBinaryCompare) <> 0 Then

            CheckNames = CheckNames + 1

            AddDetail details, _
                "Folder: expected " & expected & _
                ", got " & item.Name
        End If
    Next i

    For i = 1 To folders.Count
        Set item = folders(i)

        If Not HasFeature(active, item) Then
            unusedIndex = unusedIndex + 1
            expected = "UNUSED_CUTLIST_" & _
                       Format$(unusedIndex, "000")

            If StrComp(item.Name, expected, _
                       vbBinaryCompare) <> 0 Then

                CheckNames = CheckNames + 1

                AddDetail details, _
                    "Unused: expected " & expected & _
                    ", got " & item.Name
            End If
        End If
    Next i

    For i = 1 To bodyLists.Count
        Set list = bodyLists(i)

        For j = 1 To list.Count
            Set b = list(j)
            expected = CStr(i) & "-" & CStr(j)

            If StrComp(b.Name, expected, _
                       vbBinaryCompare) <> 0 Then

                CheckNames = CheckNames + 1

                AddDetail details, _
                    "Body: expected " & expected & _
                    ", got " & b.Name

                If swModel.FeatureManager.IsNameUsed( _
                    0, expected) Then

                    AddDetail details, _
                        "Body name " & expected & _
                        " is already used in this part."
                End If
            End If
        Next j
    Next i
End Function

Private Sub SetFeatureName( _
    ByVal item As SldWorks.Feature, _
    ByVal desired As String, _
    ByRef details As String)

    Dim actual As String
    Dim why As String

    On Error Resume Next
    Err.Clear

    item.Name = desired
    why = Err.Description

    Err.Clear
    actual = item.Name

    If Err.Number <> 0 Then
        why = why & " " & Err.Description
    End If

    On Error GoTo 0

    If StrComp(actual, desired, vbBinaryCompare) <> 0 Then
        AddDetail details, _
            "Folder " & desired & _
            " -> " & actual & " " & why
    End If
End Sub

Private Sub SetBodyName( _
    ByVal b As SldWorks.Body2, _
    ByVal desired As String, _
    ByRef details As String)

    Dim actual As String
    Dim oldName As String
    Dim why As String
    Dim fresh As SldWorks.Body2

    On Error Resume Next
    Err.Clear

    oldName = b.Name
    b.Name = desired
    why = Err.Description

    Err.Clear
    actual = b.Name

    If Err.Number <> 0 Then
        why = why & " " & Err.Description
    End If

    On Error GoTo 0

    If StrComp(actual, desired, vbBinaryCompare) <> 0 Then
        If IsNumberedBodyName(desired) Then
            If swModel.FeatureManager.IsNameUsed( _
                0, desired) Then

                ReleaseSelectedName b, desired, details

                On Error Resume Next
                Err.Clear

                b.Name = desired

                If Err.Number <> 0 Then
                    why = why & " " & Err.Description
                End If

                Err.Clear
                actual = b.Name
                On Error GoTo 0
            End If
        End If
    End If

    If StrComp(actual, desired, vbBinaryCompare) <> 0 Then
        Set fresh = FindCurrentBody(oldName)

        If Not fresh Is Nothing Then
            On Error Resume Next
            Err.Clear

            fresh.Name = desired

            If Err.Number <> 0 Then
                why = why & " " & Err.Description
            End If

            Err.Clear
            actual = fresh.Name
            On Error GoTo 0
        Else
            Set fresh = FindCurrentBody(desired)

            If Not fresh Is Nothing Then actual = desired
        End If
    End If

    If StrComp(actual, desired, vbBinaryCompare) <> 0 Then
        AddDetail details, _
            "Body " & desired & _
            " -> " & actual & " " & why
    End If
End Sub

Private Function IsNumberedBodyName( _
    ByVal value As String) As Boolean

    Dim p As Long

    p = InStr(1, value, "-", vbBinaryCompare)

    If p < 2 Or p >= Len(value) Then Exit Function

    IsNumberedBodyName = _
        IsNumeric(Left$(value, p - 1)) And _
        IsNumeric(Mid$(value, p + 1))
End Function

Private Sub ReleaseSelectedName( _
    ByVal target As SldWorks.Body2, _
    ByVal desired As String, _
    ByRef details As String)

    Dim selected As SldWorks.Body2
    Dim sm As SldWorks.SelectionMgr
    Dim safeName As String
    Dim index As Long
    Dim didSelect As Boolean

    On Error Resume Next

    swModel.ClearSelection2 True

    didSelect = swModel.Extension.SelectByID2( _
        desired, "SOLIDBODY", _
        0#, 0#, 0#, False, 0, Nothing, 0)

    If didSelect Then
        Set sm = swModel.SelectionManager
        Set selected = sm.GetSelectedObject6(1, -1)
    End If

    swModel.ClearSelection2 True
    On Error GoTo 0

    If selected Is Nothing Then
        AddDetail details, _
            "Cannot locate the body occupying " & _
            desired & " in the active configuration."
        Exit Sub
    End If

    If selected Is target Then Exit Sub

    If StrComp(selected.Name, desired, _
               vbBinaryCompare) <> 0 Then Exit Sub

    index = 1
    safeName = "UNASSIGNED_BODY_" & _
               Format$(index, "000")

    Do While swModel.FeatureManager.IsNameUsed( _
                0, safeName)

        index = index + 1
        safeName = "UNASSIGNED_BODY_" & _
                   Format$(index, "000")
    Loop

    On Error Resume Next
    selected.Name = safeName

    If Err.Number = 0 Then
        If StrComp(selected.Name, safeName, _
                   vbBinaryCompare) = 0 Then

            movedOutside = movedOutside + 1

            AddDetail details, _
                "Body occupying " & desired & _
                " moved to " & safeName
        End If
    End If

    On Error GoTo 0
End Sub

Private Function FindCurrentBody( _
    ByVal bodyName As String) As SldWorks.Body2

    Dim part As SldWorks.PartDoc
    Dim v As Variant
    Dim i As Long
    Dim candidate As SldWorks.Body2

    Set part = swModel

    v = part.GetBodies2(-1, False)

    If Not IsArray(v) Then Exit Function

    For i = LBound(v) To UBound(v)
        Set candidate = v(i)

        If StrComp(candidate.Name, bodyName, _
                   vbBinaryCompare) = 0 Then

            Set FindCurrentBody = candidate
            Exit Function
        End If
    Next i
End Function

Private Sub RemoveTemporaryBodyNames( _
    ByRef details As String)

    Dim part As SldWorks.PartDoc
    Dim v As Variant
    Dim i As Long
    Dim k As Long
    Dim candidate As SldWorks.Body2
    Dim safeName As String

    Set part = swModel
    v = part.GetBodies2(-1, False)

    If Not IsArray(v) Then Exit Sub

    For i = LBound(v) To UBound(v)
        Set candidate = v(i)

        If Left$(candidate.Name, 6) = "__RCL_" Then
            k = k + 1
            safeName = "UNRENAMED_BODY_" & _
                       Format$(k, "000")

            Do While swModel.FeatureManager.IsNameUsed( _
                        0, safeName)

                k = k + 1
                safeName = "UNRENAMED_BODY_" & _
                           Format$(k, "000")
            Loop

            SetBodyName candidate, safeName, details

            If StrComp(candidate.Name, safeName, _
                       vbBinaryCompare) = 0 Then

                AddDetail details, _
                    "Temporary name replaced by " & safeName
            Else
                AddDetail details, _
                    "FAILED to remove temporary name: " & _
                    candidate.Name
            End If
        End If
    Next i
End Sub

Private Sub IncludeActive( _
    ByVal active As Collection, _
    ByRef failures As Long, _
    ByRef details As String)

    Dim item As SldWorks.Feature
    Dim excluded As Boolean
    Dim i As Long

    For Each item In active
        i = i + 1

        On Error Resume Next
        Err.Clear

        excluded = item.ExcludeFromCutList

        If Err.Number = 0 And excluded Then
            item.ExcludeFromCutList = False
        End If

        If Err.Number <> 0 Then
            failures = failures + 1

            AddDetail details, _
                "Include Cut List " & item.Name & _
                ": " & Err.Description
        ElseIf item.ExcludeFromCutList Then
            failures = failures + 1

            AddDetail details, _
                "Cut List " & item.Name & _
                " remains excluded"
        End If

        On Error GoTo 0

        ShowProgress _
            90 + 2 * i \ active.Count, _
            "Including Cut List " & _
            CStr(i) & "/" & CStr(active.Count)
    Next item
End Sub

Private Sub UpdatePropertyN( _
    ByVal active As Collection, _
    ByRef failures As Long, _
    ByRef details As String)

    Dim item As SldWorks.Feature
    Dim mgr As SldWorks.CustomPropertyManager
    Dim expression As String
    Dim raw As String
    Dim resolved As String
    Dim wasResolved As Boolean
    Dim linked As Boolean
    Dim result As Long
    Dim filename As String
    Dim i As Long

    filename = ModelFilename()

    For Each item In active
        i = i + 1

        expression = Chr$(34) & _
                     "SW-CutListItemName@@@" & _
                     item.Name & "@" & filename & Chr$(34)

        Set mgr = Nothing
        raw = ""
        resolved = ""

        On Error Resume Next
        Err.Clear

        Set mgr = item.CustomPropertyManager

        If Not mgr Is Nothing Then
            result = mgr.Add3( _
                "N", _
                CUSTOM_INFO_TEXT, _
                expression, _
                CUSTOM_PROP_DELETE_AND_ADD)

            result = mgr.Get6( _
                "N", False, raw, resolved, _
                wasResolved, linked)
        End If

        If Err.Number <> 0 Or mgr Is Nothing Or _
           InStr(1, raw, _
                 "SW-CutListItemName@@@" & _
                 item.Name & "@", _
                 vbTextCompare) = 0 Then

            failures = failures + 1

            AddDetail details, _
                "Property N failed for Cut List " & _
                item.Name & ": " & Err.Description
        End If

        On Error GoTo 0

        ShowProgress _
            92 + 7 * i \ active.Count, _
            "Property N " & _
            CStr(i) & "/" & CStr(active.Count)
    Next item
End Sub

Private Sub BeginProgress()
    On Error Resume Next

    progressValue = 0
    progressStarted = _
        swApp.GetUserProgressBar(progressBar)

    If progressStarted Then
        progressStarted = _
            progressBar.Start(0, 100, "RenamePlusN 0%")
    End If

    On Error GoTo 0
End Sub

Private Sub ShowProgress( _
    ByVal value As Long, _
    ByVal stage As String)

    If value < progressValue Then
        value = progressValue
    End If

    If value > 100 Then value = 100

    progressValue = value

    On Error Resume Next

    If progressStarted Then
        progressBar.UpdateTitle _
            "RenamePlusN " & CStr(value) & _
            "% - " & stage

        progressBar.UpdateProgress value
    End If

    DoEvents
    On Error GoTo 0
End Sub

Private Sub EndProgress()
    On Error Resume Next

    If progressStarted Then progressBar.End

    Set progressBar = Nothing
    progressStarted = False

    On Error GoTo 0
End Sub

Private Function ModelFilename() As String
    Dim value As String
    Dim at As Long

    value = swModel.GetPathName

    If Len(value) = 0 Then
        value = swModel.GetTitle
    End If

    at = InStrRev(value, "\")

    If at > 0 Then
        value = Mid$(value, at + 1)
    End If

    If Right$(value, 1) = "*" Then
        value = Left$(value, Len(value) - 1)
    End If

    If LCase$(Right$(value, 7)) <> ".sldprt" Then
        value = value & ".SLDPRT"
    End If

    ModelFilename = value
End Function

Private Sub AddDetail( _
    ByRef details As String, _
    ByVal line As String)

    If Len(details) < 1800 Then
        details = details & line & vbCrLf
    End If
End Sub

