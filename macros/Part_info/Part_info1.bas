Attribute VB_Name = "Part_info1"
Option Explicit

Dim swApp As SldWorks.SldWorks

'=========================================================
' SETTINGS
'=========================================================

Const OPEN_REPORT_AFTER_CREATE As Boolean = True

Const REPORT_SUFFIX As String = "_MODEL_REPORT.txt"
Const REPORT_HTML_SUFFIX As String = "_MODEL_REPORT.html"

'=========================================================
' CONSTANTS
'=========================================================

Const swDocPART As Long = 1
Const swSolidBody As Long = 0

Const PI_VALUE As Double = 3.14159265358979

' swBendAllowanceTypes_e
Const BA_BEND_TABLE As Long = 1
Const BA_K_FACTOR As Long = 2
Const BA_DIRECT As Long = 3
Const BA_DEDUCTION As Long = 4
Const BA_CALC_TABLE As Long = 5
Const BA_GAUGE_TABLE As Long = 6


'=========================================================
' MAIN
'=========================================================

Sub main()

    On Error GoTo MainError

    Dim macroStartedAt As Single
    macroStartedAt = Timer

    Set swApp = Application.SldWorks


    Dim swModel As SldWorks.ModelDoc2

    Set swModel = _
        swApp.ActiveDoc


    '-----------------------------------------------------
    ' CHECK DOCUMENT
    '-----------------------------------------------------

    If swModel Is Nothing Then

        MsgBox _
            "Open a SolidWorks part.", _
            vbExclamation, _
            "Model Report"

        Exit Sub

    End If


    If swModel.GetType <> swDocPART Then

        MsgBox _
            "This macro works only with SLDPRT files.", _
            vbExclamation, _
            "Model Report"

        Exit Sub

    End If


    '-----------------------------------------------------
    ' MODEL PATH
    '-----------------------------------------------------

    Dim modelPath As String

    modelPath = _
        swModel.GetPathName


    If modelPath = "" Then

        MsgBox _
            "Save the SolidWorks part first.", _
            vbExclamation, _
            "Model Report"

        Exit Sub

    End If


    swModel.ForceRebuild3 False


    '-----------------------------------------------------
    ' UPDATE CUT LISTS
    '-----------------------------------------------------

    UpdateAllCutLists _
        swModel


    swModel.ForceRebuild3 False


    '-----------------------------------------------------
    ' OUTPUT PATH
    '-----------------------------------------------------

    Dim modelFolder As String

    modelFolder = _
        Left$( _
            modelPath, _
            InStrRev(modelPath, "\") - 1)


    Dim modelBaseName As String

    modelBaseName = _
        GetBaseFileName( _
            modelPath)


    Dim reportPath As String

    reportPath = _
        modelFolder & "\" & _
        modelBaseName & _
        REPORT_SUFFIX


    Dim htmlReportPath As String

    htmlReportPath = _
        modelFolder & "\" & _
        modelBaseName & _
        REPORT_HTML_SUFFIX


    '-----------------------------------------------------
    ' PART DOCUMENT
    '-----------------------------------------------------

    Dim swPart As SldWorks.PartDoc

    Set swPart = _
        swModel


    '-----------------------------------------------------
    ' ALL SOLID BODIES
    '-----------------------------------------------------

    Dim allBodies As Variant

    allBodies = _
        swPart.GetBodies2( _
            swSolidBody, _
            False)


    If IsEmpty(allBodies) Then

        MsgBox _
            "No solid bodies found.", _
            vbExclamation, _
            "Model Report"

        Exit Sub

    End If


    '-----------------------------------------------------
    ' REPORT HEADER
    '-----------------------------------------------------

    Dim reportText As String


    reportText = _
        "SOLIDWORKS MODEL REPORT" & vbCrLf & _
        String$(70, "=") & vbCrLf & vbCrLf & _
        "Model: " & _
        modelBaseName & vbCrLf & _
        "File: " & _
        modelPath & vbCrLf & _
        "Configuration: " & _
        swModel.ConfigurationManager.ActiveConfiguration.Name & _
        vbCrLf & _
        "Generated: " & _
        CStr(Now) & vbCrLf & _
        "Solid bodies: " & _
        CStr( _
            UBound(allBodies) - _
            LBound(allBodies) + 1) & _
        vbCrLf & vbCrLf


    reportText = _
        reportText & _
        "NOTE:" & vbCrLf & _
        "Angles are reported in degrees." & vbCrLf & _
        "Linear dimensions are reported in millimetres." & vbCrLf & _
        String$(70, "=") & _
        vbCrLf & vbCrLf


    '-----------------------------------------------------
    ' BODY / CUT LIST TRACKING
    '-----------------------------------------------------

    Dim processed As Object

    Set processed = _
        CreateObject( _
            "Scripting.Dictionary")


    Dim bodyAssigned As Object

    Set bodyAssigned = _
        CreateObject( _
            "Scripting.Dictionary")


    Dim itemNumber As Long

    itemNumber = 0


    '=====================================================
    ' FIRST:
    ' PROCESS ALL CUT LIST ITEMS
    '=====================================================

    Dim cutLists As Collection

    Set cutLists = _
        New Collection


    CollectCutListFeatures _
        swModel, _
        cutLists


    Dim i As Long


    For i = 1 To cutLists.count


        Dim cutListFeat As SldWorks.Feature

        Set cutListFeat = _
            cutLists.Item(i)


        Dim bodyFolder As SldWorks.bodyFolder

        Set bodyFolder = _
            cutListFeat.GetSpecificFeature2


        If Not bodyFolder Is Nothing Then


            Dim qty As Long

            qty = _
                bodyFolder.GetBodyCount


            If qty > 0 Then


                Dim folderBodies As Variant

                folderBodies = _
                    bodyFolder.GetBodies


                If Not IsEmpty(folderBodies) Then


                    '-------------------------------------
                    ' Build a stable key for this real part
                    ' and skip duplicate Cut List entries
                    ' that can appear more than once in
                    ' the SOLIDWORKS feature tree traversal.
                    '-------------------------------------

                    Dim cutListKey As String

                    cutListKey = _
                        GetCutListStableKey( _
                            cutListFeat, _
                            folderBodies)


                    If Not processed.Exists( _
                        cutListKey) Then


                        processed.Add _
                            cutListKey, _
                            True


                        Dim representativeBody _
                            As SldWorks.Body2


                        Set representativeBody = _
                            folderBodies( _
                                LBound(folderBodies))


                        itemNumber = _
                            itemNumber + 1


                        reportText = _
                            reportText & _
                            BuildBodyReport( _
                                itemNumber, _
                                representativeBody, _
                                cutListFeat, _
                                qty)

                    End If


                    '-------------------------------------
                    ' Mark all bodies of this Cut List.
                    ' This is done even for a duplicate
                    ' Cut List entry, so the second pass
                    ' cannot report the same body again.
                    '-------------------------------------

                    Dim b As Long


                    For b = _
                        LBound(folderBodies) _
                        To UBound(folderBodies)


                        Dim tempBody _
                            As SldWorks.Body2


                        Set tempBody = _
                            folderBodies(b)


                        If Not tempBody Is Nothing Then


                            Dim assignedKey As String

                            assignedKey = _
                                GetBodyStableKey( _
                                    tempBody)


                            If Not bodyAssigned.Exists( _
                                assignedKey) Then


                                bodyAssigned.Add _
                                    assignedKey, _
                                    True

                            End If

                        End If

                    Next b

                End If

            End If

        End If

    Next i


    '=====================================================
    ' SECOND:
    ' PROCESS BODIES NOT IN CUT LIST
    '=====================================================

    For i = _
        LBound(allBodies) _
        To UBound(allBodies)


        Dim swBody As SldWorks.Body2

        Set swBody = _
            allBodies(i)


        If Not swBody Is Nothing Then


            Dim bodyKey As String

            bodyKey = _
                GetBodyStableKey( _
                    swBody)


            If Not bodyAssigned.Exists( _
                bodyKey) Then


                bodyAssigned.Add _
                    bodyKey, _
                    True


                itemNumber = _
                    itemNumber + 1


                reportText = _
                    reportText & _
                    BuildBodyReport( _
                        itemNumber, _
                        swBody, _
                        Nothing, _
                        1)

            End If

        End If

    Next i


    '=====================================================
    ' SUMMARY
    '=====================================================

    reportText = _
        reportText & _
        vbCrLf & _
        String$(70, "=") & vbCrLf & _
        "END OF REPORT" & vbCrLf & _
        "Reported items: " & _
        CStr(itemNumber) & vbCrLf & _
        "Report preparation time: " & _
        FormatElapsedTime(macroStartedAt) & vbCrLf & _
        String$(70, "=") & vbCrLf


    '=====================================================
    ' WRITE PLAIN TXT REPORT
    '=====================================================

    If Not WriteUtf8TextFile( _
        reportPath, _
        reportText) Then


        MsgBox _
            "Cannot create text report file:" & _
            vbCrLf & _
            reportPath, _
            vbCritical, _
            "Model Report"

        Exit Sub

    End If


    '=====================================================
    ' WRITE FORMATTED COLOR HTML REPORT
    '=====================================================

    Dim htmlText As String

    htmlText = _
        ConvertReportToHtml( _
            reportText, _
            modelBaseName)


    If Not WriteUtf8TextFile( _
        htmlReportPath, _
        htmlText) Then


        MsgBox _
            "Text report was created, but the colored HTML report could not be created:" & _
            vbCrLf & _
            htmlReportPath, _
            vbExclamation, _
            "Model Report"


        Exit Sub

    End If


    '=====================================================
    ' FINISHED
    '=====================================================

    MsgBox _
        "Reports created successfully." & _
        vbCrLf & vbCrLf & _
        "Items: " & _
        CStr(itemNumber) & _
        vbCrLf & _
        "Macro execution time: " & _
        FormatElapsedTime(macroStartedAt) & _
        vbCrLf & vbCrLf & _
        "Colored report:" & _
        vbCrLf & _
        htmlReportPath & _
        vbCrLf & vbCrLf & _
        "Plain text report:" & _
        vbCrLf & _
        reportPath, _
        vbInformation, _
        "Model Report"


    '-----------------------------------------------------
    ' OPEN COLORED REPORT
    '-----------------------------------------------------

    If OPEN_REPORT_AFTER_CREATE Then


        OpenFileWithDefaultApplication _
            htmlReportPath

    End If


    Exit Sub


MainError:

    MsgBox _
        "Macro error:" & _
        vbCrLf & _
        Err.Description, _
        vbCritical, _
        "Model Report"

End Sub



'=========================================================
' ELAPSED WALL TIME (TIMER RESETS AT MIDNIGHT)
'=========================================================

Function FormatElapsedTime(ByVal startedAt As Single) As String

    Dim elapsed As Double
    Dim wholeSeconds As Long
    Dim hoursPart As Long
    Dim minutesPart As Long
    Dim secondsPart As Long

    elapsed = CDbl(Timer) - CDbl(startedAt)
    If elapsed < 0# Then elapsed = elapsed + 86400#

    wholeSeconds = CLng(Fix(elapsed + 0.5))
    hoursPart = wholeSeconds \ 3600
    minutesPart = (wholeSeconds Mod 3600) \ 60
    secondsPart = wholeSeconds Mod 60

    FormatElapsedTime = Format$(hoursPart, "00") & ":" & _
                        Format$(minutesPart, "00") & ":" & _
                        Format$(secondsPart, "00") & " (hh:mm:ss)"

End Function


'=========================================================
' STABLE KEY FOR ONE CUT LIST ITEM
'
' A Cut List feature can sometimes be reached more than
' once while recursively walking the SOLIDWORKS tree.
' This key prevents one physical part from becoming two
' report ITEMs.
'=========================================================

Function GetCutListStableKey( _
    ByVal cutListFeat As SldWorks.Feature, _
    ByVal folderBodies As Variant) _
    As String


    Dim keyText As String


    keyText = _
        LCase$( _
            Trim$( _
                cutListFeat.Name))


    If Not IsEmpty(folderBodies) Then


        If IsArray(folderBodies) Then


            Dim i As Long


            For i = _
                LBound(folderBodies) _
                To UBound(folderBodies)


                Dim oneBody _
                    As SldWorks.Body2


                Set oneBody = _
                    folderBodies(i)


                If Not oneBody Is Nothing Then


                    keyText = _
                        keyText & _
                        "|" & _
                        GetBodyStableKey( _
                            oneBody)

                End If

            Next i

        End If

    End If


    GetCutListStableKey = _
        keyText

End Function


'=========================================================
' STABLE KEY FOR ONE BODY
'=========================================================

Function GetBodyStableKey( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo NameOnly


    Dim keyText As String


    keyText = _
        LCase$( _
            Trim$( _
                swBody.Name))


    Dim boxValues As Variant


    boxValues = _
        swBody.GetBodyBox


    If Not IsEmpty(boxValues) Then


        keyText = _
            keyText & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(0)) * _
                1000#) & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(1)) * _
                1000#) & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(2)) * _
                1000#)


        keyText = _
            keyText & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(3)) * _
                1000#) & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(4)) * _
                1000#) & _
            "|" & _
            FormatNumber( _
                CDbl(boxValues(5)) * _
                1000#)

    End If


    GetBodyStableKey = _
        keyText


    Exit Function


NameOnly:

    GetBodyStableKey = _
        LCase$( _
            Trim$( _
                swBody.Name))

End Function


'=========================================================
' BUILD REPORT FOR ONE ITEM
'=========================================================

Function BuildBodyReport( _
    ByVal itemNumber As Long, _
    ByVal swBody As SldWorks.Body2, _
    ByVal cutListFeat As SldWorks.Feature, _
    ByVal qty As Long) _
    As String

    On Error GoTo ErrorHandler

    Dim txt As String
    Dim isSheetMetal As Boolean
    Dim thicknessMm As Double
    Dim defaultK As Double
    Dim defaultKFound As Boolean
    Dim allowanceType As String
    Dim kSource As String
    Dim oneValue As String
    Dim moreInfo As String

    txt = String$(70, "-") & vbCrLf & _
          "ITEM " & CStr(itemNumber) & vbCrLf & _
          String$(70, "-") & vbCrLf

    If Not cutListFeat Is Nothing Then
        txt = txt & "Cut List name       : " & _
              CleanDisplayName(cutListFeat.Name) & vbCrLf
    End If

    txt = txt & _
          "Body name           : " & swBody.Name & vbCrLf & _
          "Material            : " & _
          GetBodyMaterialName(swBody, cutListFeat) & vbCrLf & _
          "Quantity            : " & CStr(qty) & vbCrLf & _
          "Body bounding box   : " & GetBodyBoxReport(swBody) & vbCrLf

    On Error Resume Next
    isSheetMetal = swBody.isSheetMetal
    On Error GoTo ErrorHandler

    If isSheetMetal Then
        txt = txt & vbCrLf & "MAIN PRODUCTION DATA" & vbCrLf & _
              String$(50, ".") & vbCrLf

        thicknessMm = GetSheetMetalThicknessMm(swBody, cutListFeat)
        If thicknessMm > 0 Then
            txt = txt & "Thickness           : " & _
                  FormatNumber(thicknessMm) & " mm" & vbCrLf
        Else
            ' A sheet-metal body must have a thickness: keep this warning.
            txt = txt & "Thickness           : NOT FOUND" & vbCrLf
        End If

        oneValue = GetMainBendRadiusSummary(swBody)
        If IsUsefulReportValue(oneValue) Then
            txt = txt & "Bend radius         : " & oneValue & vbCrLf
        End If

        defaultKFound = GetDefaultKFactor( _
            swBody, defaultK, allowanceType, kSource)
        If defaultKFound Then
            txt = txt & "K-factor            : " & _
                  FormatNumber(defaultK) & vbCrLf
        End If

        oneValue = GetFlangeHeightSummary(swBody)
        If IsUsefulReportValue(oneValue) Then
            txt = txt & "Flange height       : " & oneValue & vbCrLf
        End If

        ' Show sections only when they contain features or diagnostic errors.
        AppendOptionalReportSection txt, "CLOSED CORNERS", _
            GetClosedCornerReport(swBody)
        AppendOptionalReportSection txt, _
            "CORNER RELIEFS / STRESS RELIEFS", _
            GetCornerReliefReport(swBody)
        AppendOptionalReportSection txt, "BEND DETAILS", _
            GetBendReport(swBody, defaultK, defaultKFound)

        If Not cutListFeat Is Nothing Then
            oneValue = GetCutListBoundingBox(cutListFeat)
            If IsUsefulReportValue(oneValue) Then
                moreInfo = moreInfo & "Cut List box        : " & _
                           oneValue & vbCrLf
            End If
        End If

        If IsUsefulReportValue(allowanceType) Then
            moreInfo = moreInfo & "Bend allowance type : " & _
                       allowanceType & vbCrLf
        End If
        If IsUsefulReportValue(kSource) Then
            moreInfo = moreInfo & "K-factor source     : " & _
                       kSource & vbCrLf
        End If

        oneValue = GetDefaultBendReliefText(swBody)
        If IsUsefulReportValue(oneValue) Then
            moreInfo = moreInfo & "Default bend relief : " & _
                       oneValue & vbCrLf
        End If
        AppendOptionalReportSection txt, "ADDITIONAL INFORMATION", _
            moreInfo
        AppendOptionalReportSection txt, _
            "SHEET METAL FEATURE LIST", _
            GetSheetMetalFeatureList(swBody)
    End If

    BuildBodyReport = txt & vbCrLf & vbCrLf
    Exit Function

ErrorHandler:
    BuildBodyReport = "ERROR READING BODY: " & Err.Description & _
                      vbCrLf & vbCrLf
End Function

'=========================================================
' OMIT EMPTY FEATURE SECTIONS WITHOUT HIDING READ ERRORS
'=========================================================

Sub AppendOptionalReportSection( _
    ByRef reportText As String, _
    ByVal sectionTitle As String, _
    ByVal sectionBody As String)

    Dim trimmedBody As String
    trimmedBody = Trim$(sectionBody)
    If Len(trimmedBody) = 0 Then Exit Sub
    If LCase$(Left$(trimmedBody, 3)) = "no " Then Exit Sub

    reportText = reportText & vbCrLf & sectionTitle & vbCrLf & _
                 String$(50, ".") & vbCrLf & sectionBody
End Sub

'=========================================================
' EMPTY AND N/A VALUES ADD NO PRODUCTION INFORMATION
'=========================================================

Function IsUsefulReportValue(ByVal reportValue As String) As Boolean
    Dim valueText As String
    valueText = UCase$(Trim$(reportValue))
    IsUsefulReportValue = Len(valueText) > 0 And _
                          valueText <> "N/A" And _
                          valueText <> "NO" And _
                          valueText <> "NOT APPLICABLE"
End Function


'=========================================================
' MAIN BEND RADIUS SUMMARY
'
' If all bends have one radius:
'   1.3 mm
'
' If radii differ:
'   1.3 mm; 2 mm
'=========================================================

Function GetMainBendRadiusSummary( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant


    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetMainBendRadiusSummary = _
            "N/A"


        Exit Function

    End If


    Dim values As Object


    Set values = _
        CreateObject( _
            "Scripting.Dictionary")


    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature


        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim typeName As String


            typeName = _
                LCase$( _
                    Trim$( _
                        feat.GetTypeName2))


            If IsBendFeatureType( _
                typeName) Or _
               typeName = _
                "sheetmetal" Or _
               typeName = _
                "smbaseflange" Then


                Dim featData As Object


                Set featData = _
                    Nothing


                On Error Resume Next


                Set featData = _
                    feat.GetDefinition


                On Error GoTo ErrorHandler


                If Not featData Is Nothing Then


                    Dim radiusM As Double


                    If TryGetDoubleProperty( _
                        featData, _
                        "BendRadius", _
                        radiusM) Then


                        If Abs(radiusM) > _
                           0.000000001 Then


                            Dim radiusText As String


                            radiusText = _
                                FormatNumber( _
                                    Abs(radiusM) * _
                                    1000#) & _
                                " mm"


                            If Not values.Exists( _
                                radiusText) Then


                                values.Add _
                                    radiusText, _
                                    True

                            End If

                        End If

                    End If

                End If

            End If

        End If

    Next i


    If values.count = 0 Then


        GetMainBendRadiusSummary = _
            "N/A"


    Else


        GetMainBendRadiusSummary = _
            JoinDictionaryKeys( _
                values, _
                "; ")

    End If


    Exit Function


ErrorHandler:

    GetMainBendRadiusSummary = _
        "N/A"

End Function


'=========================================================
' FLANGE HEIGHT SUMMARY
'
' Edge-Flange1 = 15 mm; Edge-Flange2 = 30 mm
'=========================================================

Function GetFlangeHeightSummary( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant


    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetFlangeHeightSummary = _
            "N/A"


        Exit Function

    End If


    Dim values As Collection


    Set values = _
        New Collection


    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature


        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            If LCase$( _
                Trim$( _
                    feat.GetTypeName2)) = _
               "edgeflange" Then


                Dim featData As Object


                Set featData = _
                    Nothing


                On Error Resume Next


                Set featData = _
                    feat.GetDefinition


                On Error GoTo ErrorHandler


                If Not featData Is Nothing Then


                    Dim flangeLengthM As Double


                    If TryGetDoubleProperty( _
                        featData, _
                        "OffsetDistance", _
                        flangeLengthM) Then


                        values.Add _
                            CleanDisplayName( _
                                feat.Name) & _
                            " = " & _
                            FormatNumber( _
                                Abs(flangeLengthM) * _
                                1000#) & _
                            " mm"

                    End If

                End If

            End If

        End If

    Next i


    If values.count = 0 Then


        GetFlangeHeightSummary = _
            "N/A"


    Else


        GetFlangeHeightSummary = _
            JoinCollectionStrings( _
                values, _
                "; ")

    End If


    Exit Function


ErrorHandler:

    GetFlangeHeightSummary = _
        "N/A"

End Function


'=========================================================
' JOIN DICTIONARY KEYS
'=========================================================

Function JoinDictionaryKeys( _
    ByVal dict As Object, _
    ByVal delimiter As String) _
    As String


    Dim result As String

    Dim key As Variant


    For Each key In _
        dict.Keys


        If result <> "" Then


            result = _
                result & _
                delimiter

        End If


        result = _
            result & _
            CStr(key)

    Next key


    JoinDictionaryKeys = _
        result

End Function


'=========================================================
' JOIN COLLECTION STRINGS
'=========================================================

Function JoinCollectionStrings( _
    ByVal items As Collection, _
    ByVal delimiter As String) _
    As String


    Dim result As String

    Dim i As Long


    For i = 1 To items.count


        If result <> "" Then


            result = _
                result & _
                delimiter

        End If


        result = _
            result & _
            CStr( _
                items.Item(i))

    Next i


    JoinCollectionStrings = _
        result

End Function


'=========================================================
' GET BEND REPORT
'=========================================================

Function GetBendReport( _
    ByVal swBody As SldWorks.Body2, _
    ByVal defaultK As Double, _
    ByVal defaultKFound As Boolean) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant

    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetBendReport = _
            "No body features found." & _
            vbCrLf


        Exit Function

    End If


    Dim seen As Object

    Set seen = _
        CreateObject( _
            "Scripting.Dictionary")


    Dim txt As String

    Dim bendNo As Long

    bendNo = 0


    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim typeName As String

            typeName = _
                LCase$( _
                    Trim$( _
                        feat.GetTypeName2))


            If IsBendFeatureType( _
                typeName) Then


                Dim uniqueKey As String

                uniqueKey = _
                    feat.Name & _
                    "|" & _
                    typeName


                If Not seen.Exists( _
                    uniqueKey) Then


                    seen.Add _
                        uniqueKey, _
                        True


                    bendNo = _
                        bendNo + 1


                    txt = _
                        txt & _
                        BuildBendLine( _
                            bendNo, _
                            feat, _
                            defaultK, _
                            defaultKFound)

                End If

            End If

        End If

    Next i


    If bendNo = 0 Then


        txt = _
            "No bend features detected." & _
            vbCrLf

    End If


    GetBendReport = _
        txt


    Exit Function


ErrorHandler:

    GetBendReport = _
        "Error reading bends: " & _
        Err.Description & _
        vbCrLf

End Function


'=========================================================
' BUILD ONE BEND LINE
'=========================================================

Function BuildBendLine( _
    ByVal bendNumber As Long, _
    ByVal feat As SldWorks.Feature, _
    ByVal defaultK As Double, _
    ByVal defaultKFound As Boolean) _
    As String


    On Error GoTo ErrorHandler


    Dim featData As Object

    Set featData = _
        Nothing


    On Error Resume Next

    Set featData = _
        feat.GetDefinition

    On Error GoTo ErrorHandler


    Dim typeName As String

    typeName = _
        feat.GetTypeName2


    Dim angleRad As Double

    Dim angleFound As Boolean


    Dim radiusM As Double

    Dim radiusFound As Boolean


    '-----------------------------------------------------
    ' ANGLE
    '-----------------------------------------------------

    If Not featData Is Nothing Then


        On Error Resume Next


        Err.Clear


        angleRad = _
            CDbl( _
                featData.BendAngle)


        If Err.Number = 0 Then

            angleFound = _
                True

        End If


        Err.Clear


        radiusM = _
            CDbl( _
                featData.BendRadius)


        If Err.Number = 0 Then

            radiusFound = _
                True

        End If


        On Error GoTo ErrorHandler

    End If


    '-----------------------------------------------------
    ' K FACTOR FOR THIS BEND
    '-----------------------------------------------------

    Dim bendK As Double

    Dim bendKFound As Boolean

    Dim bendKSource As String


    bendKFound = _
        GetBendKFactor( _
            feat, _
            defaultK, _
            defaultKFound, _
            bendK, _
            bendKSource)


    '-----------------------------------------------------
    ' OUTPUT
    '-----------------------------------------------------

    Dim txt As String


    txt = _
        CStr(bendNumber) & _
        ") " & _
        CleanDisplayName( _
            feat.Name) & _
        "  [" & _
        typeName & _
        "]" & _
        vbCrLf


    If angleFound Then


        txt = _
            txt & _
            "   Angle       : " & _
            FormatNumber( _
                Abs(angleRad) * _
                180# / _
                PI_VALUE) & _
            " deg" & _
            vbCrLf


    End If


    If radiusFound Then


        txt = _
            txt & _
            "   Bend radius : " & _
            FormatNumber( _
                Abs(radiusM) * _
                1000#) & _
            " mm" & _
            vbCrLf


    End If


    If bendKFound Then


        txt = _
            txt & _
            "   K-factor    : " & _
            FormatNumber( _
                bendK) & _
            " (" & _
            bendKSource & _
            ")" & _
            vbCrLf


    End If


    '-----------------------------------------------------
    ' EXTRA PRODUCTION DATA:
    ' flange length, bend length, direction, position,
    ' and bend-relief settings
    '-----------------------------------------------------

    txt = _
        txt & _
        GetBendProductionDetails( _
            feat, _
            featData, _
            LCase$(typeName))


    txt = _
        txt & _
        vbCrLf


    BuildBendLine = _
        txt


    Exit Function


ErrorHandler:

    BuildBendLine = _
        CStr(bendNumber) & _
        ") " & _
        feat.Name & _
        " - ERROR: " & _
        Err.Description & _
        vbCrLf

End Function


'=========================================================
' IS BEND FEATURE
'=========================================================

Function IsBendFeatureType( _
    ByVal typeName As String) _
    As Boolean


    Select Case LCase$( _
        typeName)


        Case "edgeflange", _
             "onebend", _
             "sketchbend", _
             "sm3dbend", _
             "toroidalbend"


            IsBendFeatureType = _
                True


        Case Else


            IsBendFeatureType = _
                False

    End Select

End Function



'=========================================================
' PRODUCTION DETAILS FOR ONE BEND / FLANGE
'=========================================================

Function GetBendProductionDetails( _
    ByVal feat As SldWorks.Feature, _
    ByVal featData As Object, _
    ByVal typeName As String) _
    As String


    On Error GoTo ErrorHandler


    If featData Is Nothing Then
        Exit Function
    End If


    Dim txt As String


    '=====================================================
    ' EDGE FLANGE:
    ' length, length direction, bend-line length,
    ' flange position and bend relief
    '=====================================================

    If LCase$(typeName) = _
       "edgeflange" Then


        Dim valueD As Double
        Dim valueL As Long
        Dim valueB As Boolean
        Dim valueFound As Boolean


        '-------------------------------------------------
        ' Flange length end condition
        '-------------------------------------------------

        valueFound = _
            TryGetLongProperty( _
                featData, _
                "OffsetType", _
                valueL)


        If valueFound Then


            txt = _
                txt & _
                "   Length type  : " & _
                FlangeOffsetTypeName( _
                    valueL) & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' Flange length
        '-------------------------------------------------

        valueFound = _
            TryGetDoubleProperty( _
                featData, _
                "OffsetDistance", _
                valueD)


        If valueFound Then


            txt = _
                txt & _
                "   Flange length: " & _
                FormatNumber( _
                    Abs(valueD) * _
                    1000#) & _
                " mm" & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' How flange length is measured
        '-------------------------------------------------

        valueFound = _
            TryGetLongProperty( _
                featData, _
                "OffsetDimType", _
                valueL)


        If valueFound Then


            txt = _
                txt & _
                "   Length origin: " & _
                FlangeDimTypeName( _
                    valueL) & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' Reverse length direction
        '-------------------------------------------------

        valueFound = _
            TryGetBooleanProperty( _
                featData, _
                "ReverseOffset", _
                valueB)


        If valueFound Then


            If valueB Then


                txt = _
                    txt & _
                    "   Length dir.  : REVERSED" & _
                    vbCrLf


            Else


                txt = _
                    txt & _
                    "   Length dir.  : NORMAL" & _
                    vbCrLf

            End If

        End If


        '-------------------------------------------------
        ' Flange position
        '-------------------------------------------------

        valueFound = _
            TryGetLongProperty( _
                featData, _
                "PositionType", _
                valueL)


        If valueFound Then


            txt = _
                txt & _
                "   Flange pos.  : " & _
                FlangePositionTypeName( _
                    valueL) & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' Actual selected bend-edge length
        '-------------------------------------------------

        Dim edgeText As String

        edgeText = _
            GetEdgeFlangeBendLengthText( _
                featData)


        If edgeText <> "" Then


            txt = _
                txt & _
                "   Bend length  : " & _
                edgeText & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' Position offset
        '-------------------------------------------------

        valueFound = _
            TryGetBooleanProperty( _
                featData, _
                "UsePositionOffset", _
                valueB)


        If valueFound Then


            If valueB Then


                Dim positionOffset As Double
                Dim reversePosition As Boolean
                Dim offsetFound As Boolean


                offsetFound = _
                    TryGetDoubleProperty( _
                        featData, _
                        "PositionOffsetDistance", _
                        positionOffset)


                If offsetFound Then


                    txt = _
                        txt & _
                        "   Pos. offset  : " & _
                        FormatNumber( _
                            Abs(positionOffset) * _
                            1000#) & _
                        " mm"

                Else


                    txt = _
                        txt & _
                        "   Pos. offset  : YES"

                End If


                If TryGetBooleanProperty( _
                    featData, _
                    "ReversePositionOffset", _
                    reversePosition) Then


                    If reversePosition Then


                        txt = _
                            txt & _
                            " (REVERSED)"

                    End If

                End If


                txt = _
                    txt & _
                    vbCrLf

            End If

        End If


        '-------------------------------------------------
        ' Trim side bends
        '-------------------------------------------------

        valueFound = _
            TryGetBooleanProperty( _
                featData, _
                "UsePositionTrimSideBends", _
                valueB)


        If valueFound Then


            txt = _
                txt & _
                "   Trim side    : " & _
                YesNoText( _
                    valueB) & _
                vbCrLf

        End If


        '-------------------------------------------------
        ' Bend relief for this flange
        '-------------------------------------------------

        txt = _
            txt & _
            GetEdgeFlangeReliefDetails( _
                featData)

    End If


    '=====================================================
    ' ONE BEND / SKETCH BEND:
    ' actual UP / DOWN direction where API exposes it
    '=====================================================

    If LCase$(typeName) = _
       "onebend" Or _
       LCase$(typeName) = _
       "sketchbend" Or _
       LCase$(typeName) = _
       "toroidalbend" Then


        Dim bendDirection As Long


        If TryGetLongProperty( _
            featData, _
            "BendDirection", _
            bendDirection) Then


            txt = _
                txt & _
                "   Bend dir.    : " & _
                BendDirectionName( _
                    bendDirection) & _
                vbCrLf

        Else


            Dim bendDown As Boolean


            If TryGetBooleanProperty( _
                featData, _
                "BendDown", _
                bendDown) Then


                If bendDown Then


                    txt = _
                        txt & _
                        "   Bend dir.    : DOWN" & _
                        vbCrLf


                Else


                    txt = _
                        txt & _
                        "   Bend dir.    : UP" & _
                        vbCrLf

                End If

            End If

        End If


        Dim bendOrder As Long


        If TryGetLongProperty( _
            featData, _
            "BendOrder", _
            bendOrder) Then


            If bendOrder > 0 Then


                txt = _
                    txt & _
                    "   Bend order   : " & _
                    CStr(bendOrder) & _
                    vbCrLf

            End If

        End If

    End If


    '=====================================================
    ' SKETCHED 3D BEND / OTHER BEND FEATURES:
    ' reversed direction where available
    '=====================================================

    If LCase$(typeName) = _
       "sm3dbend" Then


        Dim reverseDirection As Boolean


        If TryGetBooleanProperty( _
            featData, _
            "ReverseDirection", _
            reverseDirection) Then


            If reverseDirection Then


                txt = _
                    txt & _
                    "   Bend dir.    : REVERSED" & _
                    vbCrLf


            Else


                txt = _
                    txt & _
                    "   Bend dir.    : NORMAL" & _
                    vbCrLf

            End If

        End If

    End If


    GetBendProductionDetails = _
        txt


    Exit Function


ErrorHandler:

    GetBendProductionDetails = _
        ""

End Function


'=========================================================
' EDGE FLANGE BEND-LINE LENGTH
'=========================================================

Function GetEdgeFlangeBendLengthText( _
    ByVal featData As Object) _
    As String


    On Error GoTo ErrorHandler


    Dim edges As Variant


    On Error Resume Next


    edges = _
        featData.edges


    If Err.Number <> 0 Then


        Err.Clear

        GetEdgeFlangeBendLengthText = _
            ""

        Exit Function

    End If


    On Error GoTo ErrorHandler


    If IsEmpty(edges) Then
        Exit Function
    End If


    If Not IsArray(edges) Then
        Exit Function
    End If


    Dim totalLengthM As Double

    Dim edgeCount As Long

    Dim i As Long


    For i = _
        LBound(edges) _
        To UBound(edges)


        Dim swEdge As Object

        Set swEdge = _
            Nothing


        On Error Resume Next


        Set swEdge = _
            edges(i)


        If Not swEdge Is Nothing Then


            Err.Clear


            Dim oneLength As Double

            oneLength = _
                CDbl( _
                    swEdge.GetLength)


            If Err.Number = 0 Then


                totalLengthM = _
                    totalLengthM + _
                    Abs(oneLength)


                edgeCount = _
                    edgeCount + 1

            End If

        End If


        On Error GoTo ErrorHandler

    Next i


    If edgeCount > 0 Then


        GetEdgeFlangeBendLengthText = _
            FormatNumber( _
                totalLengthM * _
                1000#) & _
            " mm"


        If edgeCount > 1 Then


            GetEdgeFlangeBendLengthText = _
                GetEdgeFlangeBendLengthText & _
                " (total of " & _
                CStr(edgeCount) & _
                " edges)"

        End If

    End If


    Exit Function


ErrorHandler:

    GetEdgeFlangeBendLengthText = _
        ""

End Function


'=========================================================
' EDGE FLANGE BEND RELIEF
'=========================================================

Function GetEdgeFlangeReliefDetails( _
    ByVal featData As Object) _
    As String


    On Error GoTo ErrorHandler


    Dim txt As String

    Dim UseDefault As Boolean


    If TryGetBooleanProperty( _
        featData, _
        "UseDefaultBendRelief", _
        UseDefault) Then


        If UseDefault Then


            GetEdgeFlangeReliefDetails = _
                "   Bend relief  : DEFAULT SHEET-METAL SETTING" & _
                vbCrLf


            Exit Function

        End If

    End If


    Dim reliefType As Long


    If TryGetLongProperty( _
        featData, _
        "AutoReliefType", _
        reliefType) Then


        txt = _
            txt & _
            "   Bend relief  : " & _
            SheetMetalReliefTypeName( _
                reliefType) & _
            vbCrLf

    End If


    Dim useRatio As Boolean


    If TryGetBooleanProperty( _
        featData, _
        "UseReliefRatio", _
        useRatio) Then


        If useRatio Then


            Dim ratio As Double


            If TryGetDoubleProperty( _
                featData, _
                "ReliefRatio", _
                ratio) Then


                txt = _
                    txt & _
                    "   Relief ratio : " & _
                    FormatNumber( _
                        ratio) & _
                    vbCrLf

            End If


        Else


            Dim reliefWidth As Double
            Dim reliefDepth As Double


            If TryGetDoubleProperty( _
                featData, _
                "ReliefWidth", _
                reliefWidth) Then


                txt = _
                    txt & _
                    "   Relief width : " & _
                    FormatNumber( _
                        Abs(reliefWidth) * _
                        1000#) & _
                    " mm" & _
                    vbCrLf

            End If


            If TryGetDoubleProperty( _
                featData, _
                "ReliefDepth", _
                reliefDepth) Then


                txt = _
                    txt & _
                    "   Relief depth : " & _
                    FormatNumber( _
                        Abs(reliefDepth) * _
                        1000#) & _
                    " mm" & _
                    vbCrLf

            End If

        End If

    End If


    Dim tearType As Long


    If TryGetLongProperty( _
        featData, _
        "ReliefTearType", _
        tearType) Then


        txt = _
            txt & _
            "   Tear type    : " & _
            ReliefTearTypeName( _
                tearType) & _
            vbCrLf

    End If


    Dim gapDistance As Double


    If TryGetDoubleProperty( _
        featData, _
        "GapDistance", _
        gapDistance) Then


        If Abs(gapDistance) > 0.000000001 Then


            txt = _
                txt & _
                "   Relief gap   : " & _
                FormatNumber( _
                    Abs(gapDistance) * _
                    1000#) & _
                " mm" & _
                vbCrLf

        End If

    End If


    GetEdgeFlangeReliefDetails = _
        txt


    Exit Function


ErrorHandler:

    GetEdgeFlangeReliefDetails = _
        ""

End Function


'=========================================================
' DEFAULT SHEET-METAL BEND RELIEF
'=========================================================

Function GetDefaultBendReliefText( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant


    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetDefaultBendReliefText = _
            "N/A"


        Exit Function

    End If


    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim typeName As String

            typeName = _
                LCase$( _
                    feat.GetTypeName2)


            If typeName = _
               "sheetmetal" Or _
               typeName = _
               "smbaseflange" Then


                Dim featData As Object

                Set featData = _
                    Nothing


                On Error Resume Next


                Set featData = _
                    feat.GetDefinition


                On Error GoTo ErrorHandler


                If Not featData Is Nothing Then


                    Dim useAuto As Boolean
                    Dim useAutoFound As Boolean


                    useAutoFound = _
                        TryGetBooleanProperty( _
                            featData, _
                            "UseAutoRelief", _
                            useAuto)


                    If useAutoFound Then


                        If Not useAuto Then


                            GetDefaultBendReliefText = _
                                "NONE / automatic relief disabled"


                            Exit Function

                        End If

                    End If


                    Dim reliefType As Long


                    If Not TryGetLongProperty( _
                        featData, _
                        "AutoReliefType", _
                        reliefType) Then


                        TryGetLongProperty _
                            featData, _
                            "ReliefType", _
                            reliefType

                    End If


                    Dim result As String


                    result = _
                        SheetMetalReliefTypeName( _
                            reliefType)


                    Dim ratio As Double


                    If TryGetDoubleProperty( _
                        featData, _
                        "ReliefRatio", _
                        ratio) Then


                        If ratio > 0 Then


                            result = _
                                result & _
                                "; ratio=" & _
                                FormatNumber( _
                                    ratio)

                        End If

                    End If


                    Dim reliefWidth As Double


                    If TryGetDoubleProperty( _
                        featData, _
                        "ReliefWidth", _
                        reliefWidth) Then


                        If Abs(reliefWidth) > _
                           0.000000001 Then


                            result = _
                                result & _
                                "; width=" & _
                                FormatNumber( _
                                    Abs(reliefWidth) * _
                                    1000#) & _
                                " mm"

                        End If

                    End If


                    Dim reliefDepth As Double


                    If TryGetDoubleProperty( _
                        featData, _
                        "ReliefDepth", _
                        reliefDepth) Then


                        If Abs(reliefDepth) > _
                           0.000000001 Then


                            result = _
                                result & _
                                "; depth=" & _
                                FormatNumber( _
                                    Abs(reliefDepth) * _
                                    1000#) & _
                                " mm"

                        End If

                    End If


                    GetDefaultBendReliefText = _
                        result


                    Exit Function

                End If

            End If

        End If

    Next i


    GetDefaultBendReliefText = _
        "N/A"


    Exit Function


ErrorHandler:

    GetDefaultBendReliefText = _
        "N/A"

End Function


'=========================================================
' CLOSED CORNERS REPORT
'=========================================================

Function GetClosedCornerReport( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant


    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetClosedCornerReport = _
            "No closed-corner features detected." & _
            vbCrLf


        Exit Function

    End If


    Dim txt As String

    Dim count As Long

    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim oneText As String


            If TryBuildClosedCornerText( _
                feat, _
                count + 1, _
                oneText) Then


                count = _
                    count + 1


                txt = _
                    txt & _
                    oneText

            End If

        End If

    Next i


    If count = 0 Then


        txt = _
            "No closed-corner features detected." & _
            vbCrLf

    End If


    GetClosedCornerReport = _
        txt


    Exit Function


ErrorHandler:

    GetClosedCornerReport = _
        "Error reading closed corners: " & _
        Err.Description & _
        vbCrLf

End Function


'=========================================================
' READ ONE CLOSED CORNER FEATURE
'=========================================================

Function TryBuildClosedCornerText( _
    ByVal feat As SldWorks.Feature, _
    ByVal itemNo As Long, _
    ByRef resultText As String) _
    As Boolean


    On Error GoTo NotClosedCorner


    Dim data As Object

    Set data = _
        Nothing


    Set data = _
        feat.GetDefinition


    If data Is Nothing Then
        Exit Function
    End If


    Dim cornerType As Long
    Dim ratio As Double
    Dim gapM As Double
    Dim openBend As Boolean


    ' CornerType + OverlapUnderlapRatio together are
    ' distinctive properties of Closed Corner feature data.
    If Not TryGetLongProperty( _
        data, _
        "CornerType", _
        cornerType) Then


        Exit Function

    End If


    If cornerType < 1 Or _
       cornerType > 3 Then


        Exit Function

    End If


    If Not TryGetDoubleProperty( _
        data, _
        "OverlapUnderlapRatio", _
        ratio) Then


        Exit Function

    End If


    ' Gain selection access if supported
    On Error Resume Next

    data.AccessSelections _
        swApp.ActiveDoc, _
        Nothing

    On Error GoTo NotClosedCorner


    Dim txt As String


    txt = _
        CStr(itemNo) & _
        ") " & _
        CleanDisplayName( _
            feat.Name) & _
        "  [" & _
        feat.GetTypeName2 & _
        "]" & _
        vbCrLf


    txt = _
        txt & _
        "   Corner type  : " & _
        ClosedCornerTypeName( _
            cornerType) & _
        vbCrLf


    If TryGetDoubleProperty( _
        data, _
        "GapDistance", _
        gapM) Then


        txt = _
            txt & _
            "   Gap distance : " & _
            FormatNumber( _
                Abs(gapM) * _
                1000#) & _
            " mm" & _
            vbCrLf

    End If


    If TryGetBooleanProperty( _
        data, _
        "OpenBendRegion", _
        openBend) Then


        txt = _
            txt & _
            "   Open bend    : " & _
            YesNoText( _
                openBend) & _
            vbCrLf

    End If


    txt = _
        txt & _
        "   Overlap ratio : " & _
        FormatNumber( _
            ratio) & _
        vbCrLf & _
        vbCrLf


    On Error Resume Next

    data.ReleaseSelectionAccess

    On Error GoTo 0


    resultText = _
        txt


    TryBuildClosedCornerText = _
        True


    Exit Function


NotClosedCorner:

    On Error Resume Next

    If Not data Is Nothing Then
        data.ReleaseSelectionAccess
    End If

    On Error GoTo 0

    TryBuildClosedCornerText = _
        False

End Function


'=========================================================
' CORNER RELIEF / STRESS RELIEF REPORT
'=========================================================

Function GetCornerReliefReport( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant


    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetCornerReliefReport = _
            "No corner-relief features detected." & _
            vbCrLf


        Exit Function

    End If


    Dim txt As String

    Dim featureCount As Long

    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim oneText As String


            If TryBuildCornerReliefText( _
                feat, _
                featureCount + 1, _
                oneText) Then


                featureCount = _
                    featureCount + 1


                txt = _
                    txt & _
                    oneText

            End If

        End If

    Next i


    If featureCount = 0 Then


        txt = _
            "No corner-relief features detected." & _
            vbCrLf

    End If


    GetCornerReliefReport = _
        txt


    Exit Function


ErrorHandler:

    GetCornerReliefReport = _
        "Error reading corner reliefs: " & _
        Err.Description & _
        vbCrLf

End Function


'=========================================================
' READ ONE CORNER RELIEF FEATURE
'=========================================================

Function TryBuildCornerReliefText( _
    ByVal feat As SldWorks.Feature, _
    ByVal featureNo As Long, _
    ByRef resultText As String) _
    As Boolean


    On Error GoTo NotCornerRelief


    Dim data As Object

    Set data = _
        feat.GetDefinition


    If data Is Nothing Then
        Exit Function
    End If


    Dim cornerCount As Long


    If Not TryGetLongProperty( _
        data, _
        "CornerCount", _
        cornerCount) Then


        Exit Function

    End If


    If cornerCount <= 0 Then
        Exit Function
    End If


    On Error Resume Next

    data.AccessSelections _
        swApp.ActiveDoc, _
        Nothing

    On Error GoTo NotCornerRelief


    Dim corners As Variant


    On Error Resume Next


    Err.Clear


    corners = _
        data.GetCorners(-1)


    If Err.Number <> 0 Then


        Err.Clear

        On Error GoTo NotCornerRelief

        GoTo NotCornerRelief

    End If


    On Error GoTo NotCornerRelief


    If IsEmpty(corners) Or _
       Not IsArray(corners) Then


        GoTo NotCornerRelief

    End If


    Dim txt As String


    txt = _
        CStr(featureNo) & _
        ") " & _
        CleanDisplayName( _
            feat.Name) & _
        "  [" & _
        feat.GetTypeName2 & _
        "]" & _
        vbCrLf


    Dim i As Long


    For i = _
        LBound(corners) _
        To UBound(corners)


        Dim cornerData As Object

        Set cornerData = _
            Nothing


        Set cornerData = _
            corners(i)


        If Not cornerData Is Nothing Then


            txt = _
                txt & _
                BuildOneCornerReliefText( _
                    i - _
                    LBound(corners) + _
                    1, _
                    cornerData)

        End If

    Next i


    txt = _
        txt & _
        vbCrLf


    On Error Resume Next

    data.ReleaseSelectionAccess

    On Error GoTo 0


    resultText = _
        txt


    TryBuildCornerReliefText = _
        True


    Exit Function


NotCornerRelief:

    On Error Resume Next

    If Not data Is Nothing Then
        data.ReleaseSelectionAccess
    End If

    On Error GoTo 0

    TryBuildCornerReliefText = _
        False

End Function


'=========================================================
' ONE CORNER RELIEF DATA OBJECT
'=========================================================

Function BuildOneCornerReliefText( _
    ByVal cornerNo As Long, _
    ByVal cornerData As Object) _
    As String


    On Error GoTo ErrorHandler


    Dim txt As String

    Dim reliefType As Long


    txt = _
        "   Corner " & _
        CStr(cornerNo) & _
        vbCrLf


    If TryGetLongProperty( _
        cornerData, _
        "ReliefType", _
        reliefType) Then


        txt = _
            txt & _
            "      Relief type    : " & _
            CornerReliefTypeName( _
                reliefType) & _
            vbCrLf

    End If


    Dim ratioToThickness As Boolean


    If TryGetBooleanProperty( _
        cornerData, _
        "RatioToThickness", _
        ratioToThickness) Then

        If ratioToThickness Then


        txt = _
            txt & _
            "      Ratio/thickness: " & _
            YesNoText( _
                ratioToThickness) & _
            vbCrLf

        End If

    End If


    Dim slotLength As Double


    If TryGetDoubleProperty( _
        cornerData, _
        "SlotLength", _
        slotLength) Then


        If Abs(slotLength) > _
           0.000000001 Then


            txt = _
                txt & _
                "      Slot length    : " & _
                FormatNumber( _
                    Abs(slotLength) * _
                    1000#) & _
                " mm" & _
                vbCrLf

        End If

    End If


    Dim slotWidth As Double


    If TryGetDoubleProperty( _
        cornerData, _
        "SlotWidth", _
        slotWidth) Then


        If Abs(slotWidth) > _
           0.000000001 Then


            txt = _
                txt & _
                "      Slot width     : " & _
                FormatNumber( _
                    Abs(slotWidth) * _
                    1000#) & _
                " mm" & _
                vbCrLf

        End If

    End If


    Dim centerOnBend As Boolean


    If TryGetBooleanProperty( _
        cornerData, _
        "CenterOnBendLines", _
        centerOnBend) Then

        If centerOnBend Then


        txt = _
            txt & _
            "      Center on bends: " & _
            YesNoText( _
                centerOnBend) & _
            vbCrLf

        End If

    End If


    Dim tangentToBend As Boolean


    If TryGetBooleanProperty( _
        cornerData, _
        "TangentToBend", _
        tangentToBend) Then

        If tangentToBend Then


        txt = _
            txt & _
            "      Tangent to bend : " & _
            YesNoText( _
                tangentToBend) & _
            vbCrLf

        End If

    End If


    Dim addFillets As Boolean


    If TryGetBooleanProperty( _
        cornerData, _
        "AddFilletedCorners", _
        addFillets) Then


        If addFillets Then

            txt = txt & "      Corner fillets  : YES" & vbCrLf


            Dim filletDiameter As Double


            If TryGetDoubleProperty( _
                cornerData, _
                "CornerFilletDiameter", _
                filletDiameter) Then


                txt = _
                    txt & _
                    "      Fillet diam.    : " & _
                    FormatNumber( _
                        Abs(filletDiameter) * _
                        1000#) & _
                    " mm" & _
                    vbCrLf

            End If

        End If

    End If


    BuildOneCornerReliefText = _
        txt


    Exit Function


ErrorHandler:

    BuildOneCornerReliefText = _
        "      Error reading corner relief." & _
        vbCrLf

End Function


'=========================================================
' GENERIC LATE-BOUND PROPERTY READERS
'=========================================================

Function TryGetDoubleProperty( _
    ByVal obj As Object, _
    ByVal propertyName As String, _
    ByRef resultValue As Double) _
    As Boolean


    On Error Resume Next


    Err.Clear


    resultValue = _
        CDbl( _
            CallByName( _
                obj, _
                propertyName, _
                VbGet))


    If Err.Number = 0 Then


        TryGetDoubleProperty = _
            True


    Else


        Err.Clear

        TryGetDoubleProperty = _
            False

    End If


    On Error GoTo 0

End Function


Function TryGetLongProperty( _
    ByVal obj As Object, _
    ByVal propertyName As String, _
    ByRef resultValue As Long) _
    As Boolean


    On Error Resume Next


    Err.Clear


    resultValue = _
        CLng( _
            CallByName( _
                obj, _
                propertyName, _
                VbGet))


    If Err.Number = 0 Then


        TryGetLongProperty = _
            True


    Else


        Err.Clear

        TryGetLongProperty = _
            False

    End If


    On Error GoTo 0

End Function


Function TryGetBooleanProperty( _
    ByVal obj As Object, _
    ByVal propertyName As String, _
    ByRef resultValue As Boolean) _
    As Boolean


    On Error Resume Next


    Err.Clear


    resultValue = _
        CBool( _
            CallByName( _
                obj, _
                propertyName, _
                VbGet))


    If Err.Number = 0 Then


        TryGetBooleanProperty = _
            True


    Else


        Err.Clear

        TryGetBooleanProperty = _
            False

    End If


    On Error GoTo 0

End Function


'=========================================================
' NAME HELPERS
'=========================================================

Function BendDirectionName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            BendDirectionName = _
                "UP"


        Case 2

            BendDirectionName = _
                "DOWN"


        Case Else

            BendDirectionName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function FlangePositionTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            FlangePositionTypeName = _
                "Material inside"


        Case 2

            FlangePositionTypeName = _
                "Material outside"


        Case 3

            FlangePositionTypeName = _
                "Bend outside"


        Case 4

            FlangePositionTypeName = _
                "Bend center line"


        Case 5

            FlangePositionTypeName = _
                "Bend from virtual sharp"


        Case 6

            FlangePositionTypeName = _
                "Tangent to bend"


        Case Else

            FlangePositionTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function FlangeOffsetTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            FlangeOffsetTypeName = _
                "Blind"


        Case 2

            FlangeOffsetTypeName = _
                "Up to vertex"


        Case 3

            FlangeOffsetTypeName = _
                "Up to surface"


        Case 4

            FlangeOffsetTypeName = _
                "From surface"


        Case 5

            FlangeOffsetTypeName = _
                "Mid plane"


        Case 6

            FlangeOffsetTypeName = _
                "Up to edge and merge"


        Case Else

            FlangeOffsetTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function FlangeDimTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            FlangeDimTypeName = _
                "Outer virtual sharp"


        Case 2

            FlangeDimTypeName = _
                "Inner virtual sharp"


        Case 3

            FlangeDimTypeName = _
                "Tangent bend"


        Case Else

            FlangeDimTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function SheetMetalReliefTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            SheetMetalReliefTypeName = _
                "Rectangular"


        Case 2

            SheetMetalReliefTypeName = _
                "Tear"


        Case 3

            SheetMetalReliefTypeName = _
                "Obround"


        Case 4

            SheetMetalReliefTypeName = _
                "None"


        Case 5

            SheetMetalReliefTypeName = _
                "Tear bend"


        Case Else

            SheetMetalReliefTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function ReliefTearTypeName( _
    ByVal value As Long) _
    As String


    ' Different SOLIDWORKS releases expose tear sub-types
    ' with slightly different enum naming. Keep the raw
    ' numeric value when it is not one of the common cases.

    Select Case value


        Case 0

            ReliefTearTypeName = _
                "Default / 0"


        Case 1

            ReliefTearTypeName = _
                "Rip"


        Case 2

            ReliefTearTypeName = _
                "Extend"


        Case Else

            ReliefTearTypeName = _
                "Value " & _
                CStr(value)

    End Select

End Function


Function ClosedCornerTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 1

            ClosedCornerTypeName = _
                "Butt"


        Case 2

            ClosedCornerTypeName = _
                "Overlap"


        Case 3

            ClosedCornerTypeName = _
                "Underlap"


        Case Else

            ClosedCornerTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function CornerReliefTypeName( _
    ByVal value As Long) _
    As String


    Select Case value


        Case 0

            CornerReliefTypeName = _
                "Circular"


        Case 1

            CornerReliefTypeName = _
                "Square (legacy)"


        Case 2

            CornerReliefTypeName = _
                "Bend waist (legacy)"


        Case 3

            CornerReliefTypeName = _
                "Tear"


        Case 4

            CornerReliefTypeName = _
                "Constant width"


        Case 5

            CornerReliefTypeName = _
                "Obround"


        Case 6

            CornerReliefTypeName = _
                "Mixed (legacy)"


        Case 7

            CornerReliefTypeName = _
                "Full round"


        Case 8

            CornerReliefTypeName = _
                "Suitcase"


        Case 9

            CornerReliefTypeName = _
                "Rectangular"


        Case Else

            CornerReliefTypeName = _
                "Unknown (" & _
                CStr(value) & _
                ")"

    End Select

End Function


Function YesNoText( _
    ByVal value As Boolean) _
    As String


    If value Then


        YesNoText = _
            "YES"


    Else


        YesNoText = _
            "NO"

    End If

End Function


'=========================================================
' GET K FACTOR FOR A PARTICULAR BEND
'=========================================================

Function GetBendKFactor( _
    ByVal feat As SldWorks.Feature, _
    ByVal defaultK As Double, _
    ByVal defaultKFound As Boolean, _
    ByRef resultK As Double, _
    ByRef sourceText As String) _
    As Boolean


    On Error GoTo UseDefault


    Dim featData As Object

    Set featData = _
        feat.GetDefinition


    If featData Is Nothing Then
        GoTo UseDefault
    End If


    '-----------------------------------------------------
    ' CHECK IF FEATURE USES DEFAULT BEND ALLOWANCE
    '-----------------------------------------------------

    Dim UseDefault As Boolean

    Dim propertyAvailable As Boolean


    On Error Resume Next


    Err.Clear


    UseDefault = _
        CBool( _
            featData.UseDefaultBendAllowance)


    If Err.Number = 0 Then

        propertyAvailable = _
            True

    End If


    On Error GoTo UseDefault


    If propertyAvailable Then


        If UseDefault Then

            GoTo UseDefault

        End If

    End If


    '-----------------------------------------------------
    ' FEATURE CUSTOM BEND ALLOWANCE
    '-----------------------------------------------------

    Dim cba As Object

    Set cba = _
        Nothing


    On Error Resume Next


    Set cba = _
        featData.GetCustomBendAllowance


    On Error GoTo UseDefault


    If Not cba Is Nothing Then


        Dim allowanceType As Long

        allowanceType = _
            CLng( _
                cba.Type)


        If allowanceType = _
           BA_K_FACTOR Then


            resultK = _
                CDbl( _
                    cba.kFactor)


            sourceText = _
                "feature override"


            GetBendKFactor = _
                True


            Exit Function


        Else


            sourceText = _
                BendAllowanceTypeName( _
                    allowanceType)

        End If

    End If


UseDefault:


    If defaultKFound Then


        resultK = _
            defaultK


        sourceText = _
            "default"


        GetBendKFactor = _
            True


    Else


        GetBendKFactor = _
            False

    End If

End Function


'=========================================================
' DEFAULT K FACTOR FOR BODY
'=========================================================

Function GetDefaultKFactor( _
    ByVal swBody As SldWorks.Body2, _
    ByRef kFactor As Double, _
    ByRef allowanceTypeText As String, _
    ByRef sourceText As String) _
    As Boolean


    On Error GoTo ErrorHandler


    allowanceTypeText = _
        "Unknown"


    sourceText = _
        "Not found"


    Dim features As Variant

    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then
        Exit Function
    End If


    Dim i As Long


    '=====================================================
    ' FIRST PREFERENCE:
    ' SHEET METAL FEATURE
    '=====================================================

    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            If LCase$( _
                feat.GetTypeName2) = _
                "sheetmetal" Then


                If TryGetKFactorFromFeature( _
                    feat, _
                    kFactor, _
                    allowanceTypeText, _
                    sourceText) Then


                    GetDefaultKFactor = _
                        True


                    Exit Function

                End If

            End If

        End If

    Next i


    '=====================================================
    ' SECOND PREFERENCE:
    ' BASE FLANGE
    '=====================================================

    For i = _
        LBound(features) _
        To UBound(features)


        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            If LCase$( _
                feat.GetTypeName2) = _
                "smbaseflange" Then


                If TryGetKFactorFromFeature( _
                    feat, _
                    kFactor, _
                    allowanceTypeText, _
                    sourceText) Then


                    GetDefaultKFactor = _
                        True


                    Exit Function

                End If

            End If

        End If

    Next i


    Exit Function


ErrorHandler:

    GetDefaultKFactor = _
        False

End Function


'=========================================================
' TRY TO GET K FACTOR FROM FEATURE
'=========================================================

Function TryGetKFactorFromFeature( _
    ByVal feat As SldWorks.Feature, _
    ByRef kFactor As Double, _
    ByRef allowanceTypeText As String, _
    ByRef sourceText As String) _
    As Boolean


    On Error GoTo ErrorHandler


    Dim featData As Object

    Set featData = _
        feat.GetDefinition


    If featData Is Nothing Then
        Exit Function
    End If


    '-----------------------------------------------------
    ' CUSTOM BEND ALLOWANCE
    '-----------------------------------------------------

    Dim cba As Object

    Set cba = _
        Nothing


    On Error Resume Next


    Set cba = _
        featData.GetCustomBendAllowance


    On Error GoTo ErrorHandler


    If Not cba Is Nothing Then


        Dim allowanceType As Long

        allowanceType = _
            CLng( _
                cba.Type)


        allowanceTypeText = _
            BendAllowanceTypeName( _
                allowanceType)


        If allowanceType = _
           BA_K_FACTOR Then


            kFactor = _
                CDbl( _
                    cba.kFactor)


            sourceText = _
                CleanDisplayName( _
                    feat.Name)


            TryGetKFactorFromFeature = _
                True


            Exit Function

        End If

    End If


    '-----------------------------------------------------
    ' BASE FLANGE GAUGE TABLE / K FACTOR
    '-----------------------------------------------------

    If LCase$( _
        feat.GetTypeName2) = _
       "smbaseflange" Then


        Dim useGaugeTable As Boolean


        On Error Resume Next


        Err.Clear


        useGaugeTable = _
            CBool( _
                featData.useGaugeTable)


        If Err.Number = 0 Then


            If useGaugeTable Then


                Dim overrideK As Boolean


                Err.Clear


                overrideK = _
                    CBool( _
                        featData.OverrideKFactor)


                If Err.Number = 0 Then


                    If overrideK Then


                        kFactor = _
                            CDbl( _
                                featData.kFactor)


                        allowanceTypeText = _
                            "Gauge table / overridden K-factor"


                    Else


                        kFactor = _
                            CDbl( _
                                featData.TableKFactor)


                        allowanceTypeText = _
                            "Gauge table"

                    End If


                    sourceText = _
                        CleanDisplayName( _
                            feat.Name)


                    On Error GoTo 0


                    TryGetKFactorFromFeature = _
                        True


                    Exit Function

                End If

            End If

        End If


        '-------------------------------------------------
        ' DIRECT BASE FLANGE K FACTOR FALLBACK
        '-------------------------------------------------

        Err.Clear


        kFactor = _
            CDbl( _
                featData.kFactor)


        If Err.Number = 0 Then


            allowanceTypeText = _
                "K-factor"


            sourceText = _
                CleanDisplayName( _
                    feat.Name)


            On Error GoTo 0


            TryGetKFactorFromFeature = _
                True


            Exit Function

        End If


        On Error GoTo ErrorHandler

    End If


    TryGetKFactorFromFeature = _
        False


    Exit Function


ErrorHandler:

    TryGetKFactorFromFeature = _
        False

End Function


'=========================================================
' BEND ALLOWANCE TYPE NAME
'=========================================================

Function BendAllowanceTypeName( _
    ByVal typeValue As Long) _
    As String


    Select Case typeValue


        Case BA_BEND_TABLE

            BendAllowanceTypeName = _
                "Bend table"


        Case BA_K_FACTOR

            BendAllowanceTypeName = _
                "K-factor"


        Case BA_DIRECT

            BendAllowanceTypeName = _
                "Bend allowance"


        Case BA_DEDUCTION

            BendAllowanceTypeName = _
                "Bend deduction"


        Case BA_CALC_TABLE

            BendAllowanceTypeName = _
                "Bend calculation table"


        Case BA_GAUGE_TABLE

            BendAllowanceTypeName = _
                "Gauge table"


        Case Else

            BendAllowanceTypeName = _
                "Unknown (" & _
                CStr(typeValue) & _
                ")"

    End Select

End Function


'=========================================================
' SHEET METAL FEATURE LIST
'=========================================================

Function GetSheetMetalFeatureList( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim features As Variant

    features = _
        swBody.GetFeatures


    If IsEmpty(features) Then


        GetSheetMetalFeatureList = _
            "No features found." & _
            vbCrLf


        Exit Function

    End If


    Dim txt As String

    Dim count As Long


    Dim i As Long


    For i = _
        LBound(features) _
        To UBound(features)


        Dim feat As SldWorks.Feature

        Set feat = _
            features(i)


        If Not feat Is Nothing Then


            Dim typeName As String


            typeName = _
                feat.GetTypeName2


            If IsSheetMetalFeatureType( _
                typeName) Then


                count = _
                    count + 1


                txt = _
                    txt & _
                    CStr(count) & _
                    ") " & _
                    CleanDisplayName( _
                        feat.Name) & _
                    "  [" & _
                    typeName & _
                    "]" & _
                    vbCrLf

            End If

        End If

    Next i


    If count = 0 Then


        txt = _
            "No sheet metal features found." & _
            vbCrLf

    End If


    GetSheetMetalFeatureList = _
        txt


    Exit Function


ErrorHandler:

    GetSheetMetalFeatureList = _
        "Error reading sheet metal features." & _
        vbCrLf

End Function


'=========================================================
' IS SHEET METAL FEATURE TYPE
'=========================================================

Function IsSheetMetalFeatureType( _
    ByVal typeName As String) _
    As Boolean


    Select Case LCase$( _
        typeName)


        Case "smbaseflange", _
             "sheetmetal", _
             "edgeflange", _
             "flatpattern", _
             "flattenbends", _
             "processbends", _
             "onebend", _
             "sketchbend", _
             "sm3dbend", _
             "toroidalbend", _
             "hem", _
             "jog", _
             "loftedbend", _
             "smmiteredflange"


            IsSheetMetalFeatureType = _
                True


        Case "solidtosheetmetal", _
             "breakcorner", _
             "cornertrim", _
             "crossbreak", _
             "normalcut", _
             "formtoolinstance", _
             "smgusset", _
             "fold", _
             "unfold", _
             "closedcorner", _
             "smclosedcorner", _
             "cornerrelief", _
             "smcornerrelief"


            IsSheetMetalFeatureType = _
                True


        Case Else


            IsSheetMetalFeatureType = _
                False

    End Select

End Function


'=========================================================
' SHEET METAL THICKNESS
'=========================================================

Function GetSheetMetalThicknessMm( _
    ByVal swBody As SldWorks.Body2, _
    ByVal cutListFeat As SldWorks.Feature) _
    As Double


    On Error GoTo TryCutList


    Dim features As Variant

    features = _
        swBody.GetFeatures


    If Not IsEmpty(features) Then


        Dim i As Long


        For i = _
            LBound(features) _
            To UBound(features)


            Dim feat As SldWorks.Feature

            Set feat = _
                features(i)


            If Not feat Is Nothing Then


                Dim typeName As String

                typeName = _
                    LCase$( _
                        feat.GetTypeName2)


                If typeName = _
                   "sheetmetal" Or _
                   typeName = _
                   "smbaseflange" Then


                    Dim featData As Object

                    Set featData = _
                        Nothing


                    On Error Resume Next


                    Set featData = _
                        feat.GetDefinition


                    If Not featData Is Nothing Then


                        Dim thicknessM _
                            As Double


                        Err.Clear


                        thicknessM = _
                            CDbl( _
                                featData.Thickness)


                        If Err.Number = 0 And _
                           thicknessM > 0 Then


                            GetSheetMetalThicknessMm = _
                                thicknessM * _
                                1000#


                            On Error GoTo 0


                            Exit Function

                        End If

                    End If


                    On Error GoTo TryCutList

                End If

            End If

        Next i

    End If


TryCutList:


    If Not cutListFeat Is Nothing Then


        Dim valueText As String


        valueText = _
            GetCutListProperty( _
                cutListFeat, _
                Array( _
                    "Sheet Metal Thickness", _
                    "Thickness", _
                    "???????"))


        If valueText <> "" Then


            GetSheetMetalThicknessMm = _
                ParseNumericValue( _
                    valueText)

        End If

    End If

End Function


'=========================================================
' BODY BOUNDING BOX
'=========================================================

Function GetBodyBoxReport( _
    ByVal swBody As SldWorks.Body2) _
    As String


    On Error GoTo ErrorHandler


    Dim boxValues As Variant

    boxValues = _
        swBody.GetBodyBox


    If IsEmpty(boxValues) Then


        GetBodyBoxReport = _
            "N/A"


        Exit Function

    End If


    Dim x As Double
    Dim y As Double
    Dim z As Double


    x = _
        Abs( _
            CDbl(boxValues(3)) - _
            CDbl(boxValues(0))) * _
        1000#


    y = _
        Abs( _
            CDbl(boxValues(4)) - _
            CDbl(boxValues(1))) * _
        1000#


    z = _
        Abs( _
            CDbl(boxValues(5)) - _
            CDbl(boxValues(2))) * _
        1000#


    GetBodyBoxReport = _
        "X=" & _
        FormatNumber(x) & _
        "  Y=" & _
        FormatNumber(y) & _
        "  Z=" & _
        FormatNumber(z) & _
        " mm"


    Exit Function


ErrorHandler:

    GetBodyBoxReport = _
        "N/A"

End Function


'=========================================================
' CUT LIST BOUNDING BOX
'=========================================================

Function GetCutListBoundingBox( _
    ByVal cutListFeat As SldWorks.Feature) _
    As String


    Dim lengthText As String

    Dim widthText As String

    Dim thicknessText As String


    lengthText = _
        GetCutListProperty( _
            cutListFeat, _
            Array( _
                "Bounding Box Length", _
                "3D-Bounding Box Length"))


    widthText = _
        GetCutListProperty( _
            cutListFeat, _
            Array( _
                "Bounding Box Width", _
                "3D-Bounding Box Width"))


    thicknessText = _
        GetCutListProperty( _
            cutListFeat, _
            Array( _
                "Bounding Box Thickness", _
                "3D-Bounding Box Thickness"))


    If lengthText = "" And _
       widthText = "" And _
       thicknessText = "" Then


        GetCutListBoundingBox = _
            "N/A"


        Exit Function

    End If


    If lengthText = "" Then
        lengthText = "?"
    End If


    If widthText = "" Then
        widthText = "?"
    End If


    If thicknessText = "" Then
        thicknessText = "?"
    End If


    GetCutListBoundingBox = _
        "L=" & _
        NormalizeDimensionText(lengthText) & _
        "  W=" & _
        NormalizeDimensionText(widthText) & _
        "  T=" & _
        NormalizeDimensionText(thicknessText)

End Function


'=========================================================
' GET MATERIAL NAME FOR BODY
'
' Priority:
'   1. Resolved Cut List property "Material"
'   2. Body material visible to the user
'   3. Body material from the material database
'   4. Part-level material for the active configuration
'
' This lets the report show the effective material even
' when the body itself inherits the material from the part.
'=========================================================

Function GetBodyMaterialName( _
    ByVal swBody As SldWorks.Body2, _
    ByVal cutListFeat As SldWorks.Feature) _
    As String


    On Error GoTo TryPartMaterial


    Dim materialName As String


    materialName = ""


    '-----------------------------------------------------
    ' 1. CUT LIST PROPERTY
    '-----------------------------------------------------

    If Not cutListFeat Is Nothing Then


        materialName = _
            GetCutListProperty( _
                cutListFeat, _
                Array( _
                    "Material", _
                    "MATERIAL", _
                    "SW-Material"))


        materialName = _
            CleanMaterialName( _
                materialName)


        If materialName <> "" Then


            GetBodyMaterialName = _
                materialName


            Exit Function

        End If

    End If


    '-----------------------------------------------------
    ' 2. BODY MATERIAL - USER-VISIBLE NAME
    '-----------------------------------------------------

    On Error Resume Next


    materialName = _
        swBody.GetMaterialUserName2


    On Error GoTo TryPartMaterial


    materialName = _
        CleanMaterialName( _
            materialName)


    If materialName <> "" Then


        GetBodyMaterialName = _
            materialName


        Exit Function

    End If


    '-----------------------------------------------------
    ' 3. BODY MATERIAL - DATABASE NAME
    '-----------------------------------------------------

    Dim swModel As SldWorks.ModelDoc2
    Dim configName As String
    Dim databaseName As String


    Set swModel = _
        swApp.ActiveDoc


    If Not swModel Is Nothing Then


        configName = _
            swModel.ConfigurationManager.ActiveConfiguration.Name

    Else


        configName = ""

    End If


    databaseName = ""


    On Error Resume Next


    materialName = _
        swBody.GetMaterialPropertyName( _
            configName, _
            databaseName)


    If Trim$(materialName) = "" Then


        databaseName = ""


        materialName = _
            swBody.GetMaterialPropertyName( _
                "", _
                databaseName)

    End If


    On Error GoTo TryPartMaterial


    materialName = _
        CleanMaterialName( _
            materialName)


    If materialName <> "" Then


        GetBodyMaterialName = _
            materialName


        Exit Function

    End If


TryPartMaterial:


    '-----------------------------------------------------
    ' 4. PART-LEVEL MATERIAL
    '-----------------------------------------------------

    On Error Resume Next


    Dim swModel2 As SldWorks.ModelDoc2
    Dim swPart As SldWorks.PartDoc
    Dim activeConfigName As String
    Dim partDatabaseName As String


    Set swModel2 = _
        swApp.ActiveDoc


    If Not swModel2 Is Nothing Then


        Set swPart = _
            swModel2


        activeConfigName = _
            swModel2.ConfigurationManager.ActiveConfiguration.Name


        materialName = _
            swPart.MaterialUserName


        materialName = _
            CleanMaterialName( _
                materialName)


        If materialName = "" Then


            partDatabaseName = ""


            materialName = _
                swPart.GetMaterialPropertyName2( _
                    activeConfigName, _
                    partDatabaseName)


            If Trim$(materialName) = "" Then


                partDatabaseName = ""


                materialName = _
                    swPart.GetMaterialPropertyName2( _
                        "", _
                        partDatabaseName)

            End If


            materialName = _
                CleanMaterialName( _
                    materialName)

        End If

    End If


    On Error GoTo 0


    If materialName = "" Then


        materialName = _
            "NOT ASSIGNED"

    End If


    GetBodyMaterialName = _
        materialName

End Function


'=========================================================
' CLEAN MATERIAL NAME
'=========================================================

Function CleanMaterialName( _
    ByVal materialName As String) _
    As String


    materialName = _
        Trim$( _
            materialName)


    If materialName = "" Then


        CleanMaterialName = ""


        Exit Function

    End If


    Dim upperName As String


    upperName = _
        UCase$( _
            materialName)


    If InStr( _
        1, _
        upperName, _
        "NOT SPECIFIED", _
        vbTextCompare) > 0 Or _
       InStr( _
        1, _
        upperName, _
        "<NOT SPECIFIED>", _
        vbTextCompare) > 0 Then


        CleanMaterialName = ""


        Exit Function

    End If


    CleanMaterialName = _
        materialName

End Function


'=========================================================
' READ CUT LIST PROPERTY
'=========================================================

Function GetCutListProperty( _
    ByVal cutListFeat As SldWorks.Feature, _
    ByVal propertyNames As Variant) _
    As String


    On Error GoTo ErrorHandler


    Dim propMgr _
        As SldWorks.CustomPropertyManager


    Set propMgr = _
        cutListFeat.CustomPropertyManager


    If propMgr Is Nothing Then
        Exit Function
    End If


    Dim i As Long


    For i = _
        LBound(propertyNames) _
        To UBound(propertyNames)


        Dim rawValue As String
        Dim resolvedValue As String


        rawValue = ""

        resolvedValue = ""


        propMgr.Get2 _
            CStr(propertyNames(i)), _
            rawValue, _
            resolvedValue


        If Trim$( _
            resolvedValue) <> "" Then


            GetCutListProperty = _
                Trim$( _
                    resolvedValue)


            Exit Function

        End If


        If Trim$( _
            rawValue) <> "" Then


            GetCutListProperty = _
                Trim$( _
                    rawValue)


            Exit Function

        End If

    Next i


    Exit Function


ErrorHandler:

    GetCutListProperty = ""

End Function


'=========================================================
' NORMALIZE DIMENSION TEXT
'=========================================================

Function NormalizeDimensionText( _
    ByVal txt As String) _
    As String


    txt = _
        Trim$(txt)


    txt = _
        Replace$( _
            txt, _
            ",", _
            ".")


    If InStr( _
        1, _
        LCase$(txt), _
        "mm") = 0 Then


        txt = _
            txt & _
            " mm"

    End If


    NormalizeDimensionText = _
        txt

End Function


'=========================================================
' COLLECT CUT LIST FEATURES
'=========================================================

Sub CollectCutListFeatures( _
    ByVal swModel As SldWorks.ModelDoc2, _
    ByRef result As Collection)


    Dim feat As SldWorks.Feature

    Set feat = _
        swModel.FirstFeature


    Do While Not feat Is Nothing


        CollectCutListBranch _
            feat, _
            result


        Set feat = _
            feat.GetNextFeature

    Loop

End Sub


Sub CollectCutListBranch( _
    ByVal feat As SldWorks.Feature, _
    ByRef result As Collection)


    If LCase$( _
        feat.GetTypeName2) = _
       "cutlistfolder" Then


        Dim bf As SldWorks.bodyFolder

        Set bf = _
            feat.GetSpecificFeature2


        If Not bf Is Nothing Then


            If bf.GetBodyCount > 0 Then


                result.Add _
                    feat

            End If

        End If

    End If


    Dim subFeat As SldWorks.Feature

    Set subFeat = _
        feat.GetFirstSubFeature


    Do While Not subFeat Is Nothing


        CollectCutListBranch _
            subFeat, _
            result


        Set subFeat = _
            subFeat.GetNextSubFeature

    Loop

End Sub


'=========================================================
' UPDATE ALL CUT LISTS
'=========================================================

Sub UpdateAllCutLists( _
    ByVal swModel As SldWorks.ModelDoc2)


    On Error Resume Next


    Dim feat As SldWorks.Feature

    Set feat = _
        swModel.FirstFeature


    Do While Not feat Is Nothing


        UpdateCutListBranch _
            feat


        Set feat = _
            feat.GetNextFeature

    Loop


    On Error GoTo 0

End Sub


Sub UpdateCutListBranch( _
    ByVal feat As SldWorks.Feature)


    On Error Resume Next


    If LCase$( _
        feat.GetTypeName2) = _
       "cutlistfolder" Then


        Dim bodyFolder _
            As SldWorks.bodyFolder


        Set bodyFolder = _
            feat.GetSpecificFeature2


        If Not bodyFolder Is Nothing Then


            bodyFolder.UpdateCutList

        End If

    End If


    Dim subFeat As SldWorks.Feature

    Set subFeat = _
        feat.GetFirstSubFeature


    Do While Not subFeat Is Nothing


        UpdateCutListBranch _
            subFeat


        Set subFeat = _
            subFeat.GetNextSubFeature

    Loop


    On Error GoTo 0

End Sub


'=========================================================
' CLEAN DISPLAY NAME
'=========================================================

Function CleanDisplayName( _
    ByVal txt As String) _
    As String


    txt = _
        Replace$( _
            txt, _
            "<", _
            "_")


    txt = _
        Replace$( _
            txt, _
            ">", _
            "")


    Do While InStr( _
        1, _
        txt, _
        "__", _
        vbBinaryCompare) > 0


        txt = _
            Replace$( _
                txt, _
                "__", _
                "_")

    Loop


    CleanDisplayName = _
        Trim$(txt)

End Function


'=========================================================
' FORMAT NUMBER
'=========================================================

Function FormatNumber( _
    ByVal value As Double) _
    As String


    Dim txt As String


    txt = _
        Format$( _
            value, _
            "0.###")


    txt = _
        Replace$( _
            txt, _
            ",", _
            ".")


    FormatNumber = _
        txt

End Function


'=========================================================
' PARSE NUMERIC VALUE
'=========================================================

Function ParseNumericValue( _
    ByVal txt As String) _
    As Double


    On Error GoTo ErrorHandler


    txt = _
        Trim$(txt)


    txt = _
        Replace$( _
            txt, _
            ",", _
            ".")


    Dim numberText As String

    Dim started As Boolean

    Dim i As Long

    Dim ch As String


    For i = 1 To Len(txt)


        ch = _
            Mid$( _
                txt, _
                i, _
                1)


        If ch >= "0" And _
           ch <= "9" Then


            numberText = _
                numberText & _
                ch


            started = _
                True


        ElseIf ch = "." Then


            If started Then


                If InStr( _
                    1, _
                    numberText, _
                    ".") = 0 Then


                    numberText = _
                        numberText & _
                        "."

                End If

            End If


        Else


            If started Then
                Exit For
            End If

        End If

    Next i


    If numberText <> "" Then


        ParseNumericValue = _
            Val( _
                numberText)

    End If


    Exit Function


ErrorHandler:

    ParseNumericValue = 0

End Function



'=========================================================
' CONVERT PLAIN REPORT TO COLORED HTML
'
' The plain TXT report is kept unchanged.
' This function creates a second, formatted HTML report
' with highlighted production-critical information.
'=========================================================

Function ConvertReportToHtml( _
    ByVal plainText As String, _
    ByVal modelName As String) _
    As String


    Dim html As String


    html = _
        "<!DOCTYPE html>" & vbCrLf & _
        "<html><head><meta charset=""utf-8"">" & vbCrLf & _
        "<meta name=""viewport"" content=""width=device-width, initial-scale=1"">" & _
        vbCrLf


    html = _
        html & _
        "<title>" & _
        HtmlEncode(modelName) & _
        " - SOLIDWORKS Model Report</title>" & _
        vbCrLf & _
        "<style>" & _
        vbCrLf


    html = _
        html & _
        "body{font-family:Segoe UI,Arial,sans-serif;background:#eef1f4;color:#202124;margin:0;padding:24px;}" & _
        vbCrLf & _
        ".report{max-width:1200px;margin:auto;}" & _
        vbCrLf & _
        ".title{background:#1f4e78;color:white;padding:18px 22px;font-size:24px;font-weight:700;border-radius:10px 10px 0 0;}" & _
        vbCrLf


    html = _
        html & _
        ".modelInfo{background:white;border:1px solid #cfd8e3;border-top:none;padding:14px 20px;margin-bottom:22px;border-radius:0 0 10px 10px;}" & _
        vbCrLf & _
        ".part{background:white;border:2px solid #9aa9b8;border-radius:10px;margin:0 0 24px 0;overflow:hidden;box-shadow:0 2px 7px rgba(0,0,0,.06);}" & _
        vbCrLf & _
        ".partHeader{background:#dbe7f3;color:#163a5f;padding:9px 14px;font-family:Consolas,'Courier New',monospace;font-size:18px;font-weight:700;border-bottom:1px solid #9aa9b8;}" & _
        vbCrLf


    html = _
        html & _
        ".partBody{padding:12px 16px 16px 16px;}" & _
        vbCrLf & _
        ".line{font-family:Consolas,'Courier New',monospace;white-space:pre-wrap;font-size:14px;line-height:1.5;color:#202124;}" & _
        vbCrLf & _
        ".prodValue{color:#16823b;font-size:16px;font-weight:700;}" & _
        vbCrLf & _
        ".reliefValue{color:#16823b;font-size:16px;font-weight:700;}" & _
        vbCrLf & _
        ".partName{color:#1565c0;font-size:15px;font-weight:700;}" & _
        vbCrLf & _
        ".footer{background:white;border:1px solid #cfd8e3;border-radius:10px;padding:12px 18px;margin-top:12px;}" & _
        vbCrLf & _
        "@media print{body{background:white;padding:0}.part{box-shadow:none;page-break-inside:avoid}.title{color:black;background:white;border:1px solid #999}.partHeader{background:white;color:black}}" & _
        vbCrLf & _
        "</style></head><body>" & _
        vbCrLf


    html = _
        html & _
        "<div class=""report"">" & _
        vbCrLf & _
        "<div class=""title"">SOLIDWORKS MODEL REPORT</div>" & _
        vbCrLf


    Dim normalized As String


    normalized = _
        Replace$( _
            plainText, _
            vbCrLf, _
            vbLf)


    normalized = _
        Replace$( _
            normalized, _
            vbCr, _
            vbLf)


    Dim lines As Variant


    lines = _
        Split( _
            normalized, _
            vbLf)


    Dim i As Long
    Dim inPart As Boolean
    Dim modelInfoOpen As Boolean
    Dim footerOpen As Boolean


    modelInfoOpen = _
        True


    html = _
        html & _
        "<div class=""modelInfo"">" & _
        vbCrLf


    For i = _
        LBound(lines) _
        To UBound(lines)


        Dim oneLine As String
        Dim trimmedLine As String
        Dim upperLine As String


        oneLine = _
            CStr(lines(i))


        trimmedLine = _
            Trim$( _
                oneLine)


        upperLine = _
            UCase$( _
                trimmedLine)


        '-------------------------------------------------
        ' Start of a real part / report ITEM
        '-------------------------------------------------

        If Left$( _
            upperLine, _
            5) = _
           "ITEM " Then


            If modelInfoOpen Then


                html = _
                    html & _
                    "</div>" & _
                    vbCrLf


                modelInfoOpen = _
                    False

            End If


            If footerOpen Then


                html = _
                    html & _
                    "</div>" & _
                    vbCrLf


                footerOpen = _
                    False

            End If


            If inPart Then


                html = _
                    html & _
                    "</div></div>" & _
                    vbCrLf

            End If


            html = _
                html & _
                "<div class=""part"">" & _
                vbCrLf & _
                "<div class=""partHeader"">" & _
                HtmlEncode(oneLine) & _
                "</div>" & _
                vbCrLf & _
                "<div class=""partBody"">" & _
                vbCrLf


            inPart = _
                True


        ElseIf upperLine = _
               "END OF REPORT" Then


            If inPart Then


                html = _
                    html & _
                    "</div></div>" & _
                    vbCrLf


                inPart = _
                    False

            End If


            If modelInfoOpen Then


                html = _
                    html & _
                    "</div>" & _
                    vbCrLf


                modelInfoOpen = _
                    False

            End If


            If Not footerOpen Then


                html = _
                    html & _
                    "<div class=""footer"">" & _
                    vbCrLf


                footerOpen = _
                    True

            End If


            html = _
                html & _
                "<div class=""line"">" & _
                HtmlEncode(oneLine) & _
                "</div>" & _
                vbCrLf


        Else


            ' Skip only the long separator directly around
            ' ITEM blocks; all useful report text is kept.
            If Not IsPureReportSeparator( _
                trimmedLine) Then


                html = _
                    html & _
                    "<div class=""line"">" & _
                    FormatReportLineHtml(oneLine) & _
                    "</div>" & _
                    vbCrLf

            End If

        End If

    Next i


    If inPart Then


        html = _
            html & _
            "</div></div>" & _
            vbCrLf

    End If


    If modelInfoOpen Then


        html = _
            html & _
            "</div>" & _
            vbCrLf

    End If


    If footerOpen Then


        html = _
            html & _
            "</div>" & _
            vbCrLf

    End If


    html = _
        html & _
        "</div></body></html>"


    ConvertReportToHtml = _
        html

End Function



'=========================================================
' FORMAT ONE REPORT LINE FOR HTML
'
' Only the HTML report is affected.
' The TXT report remains unchanged.
'
' Green, slightly larger values:
'   Thickness
'   Bend radius
'   K-factor
'   Flange height
'   Relief type
'
' Blue part/body names:
'   Cut List name
'   Body name
'=========================================================

Function FormatReportLineHtml( _
    ByVal lineText As String) _
    As String


    Dim trimmedLine As String
    Dim upperLine As String


    trimmedLine = _
        Trim$( _
            lineText)


    upperLine = _
        UCase$( _
            trimmedLine)


    '-----------------------------------------------------
    ' Production values - GREEN, slightly larger
    '-----------------------------------------------------

    If Left$(upperLine, 10) = "THICKNESS " Or _
       Left$(upperLine, 11) = "BEND RADIUS " Or _
       Left$(upperLine, 9) = "K-FACTOR " Or _
       Left$(upperLine, 14) = "FLANGE HEIGHT " Or _
       Left$(upperLine, 17) = "BODY BOUNDING BOX" Or _
       Left$(upperLine, 12) = "CUT LIST BOX" Then


        FormatReportLineHtml = _
            FormatValueAfterColon( _
                lineText, _
                "prodValue")


        Exit Function

    End If


    '-----------------------------------------------------
    ' Relief type value - GREEN, slightly larger
    '-----------------------------------------------------

    If Left$(upperLine, 11) = "RELIEF TYPE" Then


        FormatReportLineHtml = _
            FormatValueAfterColon( _
                lineText, _
                "reliefValue")


        Exit Function

    End If


    '-----------------------------------------------------
    ' Part/body names - BLUE
    '-----------------------------------------------------

    If Left$(upperLine, 13) = "CUT LIST NAME" Or _
       Left$(upperLine, 9) = "BODY NAME" Then


        FormatReportLineHtml = _
            FormatValueAfterColon( _
                lineText, _
                "partName")


        Exit Function

    End If


    FormatReportLineHtml = _
        HtmlEncode( _
            lineText)

End Function


'=========================================================
' FORMAT VALUE AFTER COLON
'=========================================================

Function FormatValueAfterColon( _
    ByVal lineText As String, _
    ByVal cssClass As String) _
    As String


    Dim colonPos As Long


    colonPos = _
        InStr( _
            1, _
            lineText, _
            ":", _
            vbBinaryCompare)


    If colonPos <= 0 Then


        FormatValueAfterColon = _
            HtmlEncode( _
                lineText)


        Exit Function

    End If


    Dim prefixText As String
    Dim valueText As String


    prefixText = _
        Left$( _
            lineText, _
            colonPos)


    valueText = _
        Mid$( _
            lineText, _
            colonPos + 1)


    FormatValueAfterColon = _
        HtmlEncode(prefixText) & _
        "<span class=""" & _
        cssClass & _
        """>" & _
        HtmlEncode(valueText) & _
        "</span>"

End Function


'=========================================================
' REPORT SEPARATOR TEST
'=========================================================

Function IsPureReportSeparator( _
    ByVal txt As String) _
    As Boolean


    If Len(txt) < 20 Then
        Exit Function
    End If


    Dim firstChar As String


    firstChar = _
        Left$( _
            txt, _
            1)


    If firstChar <> "=" And _
       firstChar <> "-" And _
       firstChar <> "." Then


        Exit Function

    End If


    Dim i As Long


    For i = 1 To Len(txt)


        If Mid$( _
            txt, _
            i, _
            1) <> firstChar Then


            Exit Function

        End If

    Next i


    IsPureReportSeparator = _
        True

End Function


'=========================================================
' SELECT COLOR / STYLE FOR REPORT LINE
'=========================================================

Function GetReportLineCssClass( _
    ByVal lineText As String) _
    As String


    Dim t As String


    t = _
        Trim$( _
            lineText)


    Dim u As String


    u = _
        UCase$( _
            t)


    '-----------------------------------------------------
    ' Strong error highlighting
    '-----------------------------------------------------

    If InStr( _
        1, _
        u, _
        "ERROR", _
        vbTextCompare) > 0 Or _
       InStr( _
        1, _
        u, _
        "NOT FOUND", _
        vbTextCompare) > 0 Then


        GetReportLineCssClass = _
            "warning"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Item headers
    '-----------------------------------------------------

    If Left$( _
        u, _
        5) = _
       "ITEM " Then


        GetReportLineCssClass = _
            "item"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Main production sections
    '-----------------------------------------------------

    If u = _
       "BENDS / BEND FEATURES" Or _
       u = _
       "CLOSED CORNERS" Or _
       u = _
       "CORNER RELIEFS / STRESS RELIEFS" Or _
       u = _
       "SHEET METAL FEATURE LIST" Then


        GetReportLineCssClass = _
            "section"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Sheet metal YES / NO
    '-----------------------------------------------------

    If InStr( _
        1, _
        u, _
        "SHEET METAL", _
        vbTextCompare) > 0 Then


        If InStr( _
            1, _
            u, _
            "YES", _
            vbTextCompare) > 0 Then


            GetReportLineCssClass = _
                "sheetYes"


        Else


            GetReportLineCssClass = _
                "sheetNo"

        End If


        Exit Function

    End If


    '-----------------------------------------------------
    ' Thickness
    '-----------------------------------------------------

    If InStr( _
        1, _
        u, _
        "THICKNESS", _
        vbTextCompare) > 0 Then


        GetReportLineCssClass = _
            "thickness"


        Exit Function

    End If


    '-----------------------------------------------------
    ' K-factor / bend allowance
    '-----------------------------------------------------

    If InStr( _
        1, _
        u, _
        "K-FACTOR", _
        vbTextCompare) > 0 Or _
       InStr( _
        1, _
        u, _
        "BEND ALLOWANCE", _
        vbTextCompare) > 0 Then


        GetReportLineCssClass = _
            "kfactor"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Bend production data
    '-----------------------------------------------------

    If IsBendHighlightLine( _
        u) Then


        GetReportLineCssClass = _
            "bend"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Corner / relief information
    '-----------------------------------------------------

    If IsCornerHighlightLine( _
        u) Then


        GetReportLineCssClass = _
            "corner"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Dimensions / quantity
    '-----------------------------------------------------

    If InStr( _
        1, _
        u, _
        "BOUNDING BOX", _
        vbTextCompare) > 0 Or _
       InStr( _
        1, _
        u, _
        "CUT LIST BOX", _
        vbTextCompare) > 0 Or _
       InStr( _
        1, _
        u, _
        "QUANTITY", _
        vbTextCompare) > 0 Then


        GetReportLineCssClass = _
            "dimension"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Numbered feature / bend / corner lines
    '-----------------------------------------------------

    If IsNumberedReportLine( _
        t) Then


        GetReportLineCssClass = _
            "numbered"


        Exit Function

    End If


    '-----------------------------------------------------
    ' Separator / note lines
    '-----------------------------------------------------

    If t = "" Or _
       Left$(t, 4) = _
       "...." Or _
       Left$(t, 4) = _
       "----" Or _
       Left$(t, 4) = _
       "====" Or _
       Left$(u, 5) = _
       "NOTE:" Then


        GetReportLineCssClass = _
            "muted"


        Exit Function

    End If


    GetReportLineCssClass = _
        "line"

End Function



'=========================================================
' BEND HIGHLIGHT TEST
'=========================================================

Function IsBendHighlightLine( _
    ByVal upperText As String) _
    As Boolean


    If InStr( _
        1, _
        upperText, _
        "ANGLE", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "BEND RADIUS", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "BEND LENGTH", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "FLANGE LENGTH", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "BEND DIR.", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "LENGTH DIR.", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "FLANGE POS.", _
        vbTextCompare) > 0 Then


        IsBendHighlightLine = _
            True

        Exit Function

    End If

End Function


'=========================================================
' CORNER / RELIEF HIGHLIGHT TEST
'=========================================================

Function IsCornerHighlightLine( _
    ByVal upperText As String) _
    As Boolean


    If InStr( _
        1, _
        upperText, _
        "CORNER TYPE", _
        vbTextCompare) > 0 Then


        IsCornerHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "RELIEF", _
        vbTextCompare) > 0 Then


        IsCornerHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "GAP DISTANCE", _
        vbTextCompare) > 0 Then


        IsCornerHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "OPEN BEND", _
        vbTextCompare) > 0 Then


        IsCornerHighlightLine = _
            True

        Exit Function

    End If


    If InStr( _
        1, _
        upperText, _
        "OVERLAP RATIO", _
        vbTextCompare) > 0 Then


        IsCornerHighlightLine = _
            True

        Exit Function

    End If

End Function


'=========================================================
' DETECT NUMBERED REPORT LINE
'=========================================================

Function IsNumberedReportLine( _
    ByVal txt As String) _
    As Boolean


    If txt = "" Then
        Exit Function
    End If


    Dim i As Long


    For i = 1 To Len(txt)


        Dim ch As String


        ch = _
            Mid$( _
                txt, _
                i, _
                1)


        If ch >= "0" And _
           ch <= "9" Then


            ' continue


        ElseIf ch = ")" Then


            If i > 1 Then


                IsNumberedReportLine = _
                    True

            End If


            Exit Function


        Else


            Exit Function

        End If

    Next i

End Function


'=========================================================
' HTML ENCODE
'=========================================================

Function HtmlEncode( _
    ByVal txt As String) _
    As String


    txt = _
        Replace$( _
            txt, _
            "&", _
            "&amp;")


    txt = _
        Replace$( _
            txt, _
            "<", _
            "&lt;")


    txt = _
        Replace$( _
            txt, _
            ">", _
            "&gt;")


    txt = _
        Replace$( _
            txt, _
            """", _
            "&quot;")


    HtmlEncode = _
        txt

End Function


'=========================================================
' OPEN FILE WITH WINDOWS DEFAULT APPLICATION
'=========================================================

Sub OpenFileWithDefaultApplication( _
    ByVal filePath As String)


    On Error Resume Next


    Dim shellObj As Object


    Set shellObj = _
        CreateObject( _
            "Shell.Application")


    shellObj.ShellExecute _
        filePath, _
        "", _
        "", _
        "open", _
        1


    Set shellObj = _
        Nothing


    On Error GoTo 0

End Sub


'=========================================================
' FILE BASE NAME
'=========================================================

Function GetBaseFileName( _
    ByVal fullPath As String) _
    As String


    Dim fileName As String


    fileName = _
        Mid$( _
            fullPath, _
            InStrRev( _
                fullPath, _
                "\") + 1)


    If InStrRev( _
        fileName, _
        ".") > 0 Then


        fileName = _
            Left$( _
                fileName, _
                InStrRev( _
                    fileName, _
                    ".") - 1)

    End If


    GetBaseFileName = _
        fileName

End Function


'=========================================================
' WRITE UTF-8 TEXT FILE
'=========================================================

Function WriteUtf8TextFile( _
    ByVal filePath As String, _
    ByVal textData As String) _
    As Boolean


    On Error GoTo ErrorHandler


    Dim stream As Object


    Set stream = _
        CreateObject( _
            "ADODB.Stream")


    stream.Type = 2

    stream.Charset = _
        "utf-8"


    stream.Open


    stream.WriteText _
        textData


    stream.SaveToFile _
        filePath, _
        2


    stream.Close


    Set stream = _
        Nothing


    WriteUtf8TextFile = _
        True


    Exit Function


ErrorHandler:

    On Error Resume Next


    If Not stream Is Nothing Then

        stream.Close

    End If


    Set stream = Nothing


    WriteUtf8TextFile = _
        False

End Function



