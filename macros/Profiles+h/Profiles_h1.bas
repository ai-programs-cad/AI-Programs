Attribute VB_Name = "Profiles_h1"
Option Explicit

' ============================================================
' SOLIDWORKS 2025
' PRODUCTION CUT TABLES
'
' H = REAL PROFILE HEIGHT ABOVE THE SAW TABLE
'
' EXAMPLE:
'
' Profile 40 x 20
'
' profile lies on 40 mm face -> H = 20
' profile lies on 20 mm face -> H = 40
'
' Therefore the geometric cut-plane dimension is converted
' to the OTHER section dimension.
'
' OUT OF PLANE / COMPLEX:
'
' SEE PART DRAWING
'
' ============================================================


Private Const swDocPART As Long = 1
Private Const swDocDRAWING As Long = 3

Private Const TABLE_ANCHOR_TOPLEFT As Long = 1

Private Const swLW_THIN As Long = 0
Private Const swLW_NORMAL As Long = 1

Private Const swTextJustificationCenter As Long = 2


' ============================================================
' TABLE SIZE
' ============================================================

Private Const COL_CUTLIST_WIDTH As Double = 0.03
Private Const COL_LENGTH_WIDTH As Double = 0.03
Private Const COL_QTY_WIDTH As Double = 0.02

Private Const TABLE_TOTAL_WIDTH As Double = 0.08


' ============================================================
' ROW HEIGHTS
' ============================================================

Private Const ROW_NUMBER_HEIGHT As Double = 0.009
Private Const ROW_MATERIAL_HEIGHT As Double = 0.008
Private Const ROW_PROFILE_HEIGHT As Double = 0.02

Private Const ROW_WARNING_HEIGHT As Double = 0.01

Private Const ROW_HEADER_HEIGHT As Double = 0.008
Private Const ROW_DATA_HEIGHT As Double = 0.008


' ============================================================
' FONT
' ============================================================

Private Const NUMBER_FONT_POINTS As Double = 11#
Private Const MATERIAL_FONT_POINTS As Double = 9#

Private Const DIAGRAM_FONT_POINTS As Double = 10#
Private Const DIAGRAM_CHAR_SPACING As Double = 0.48

Private Const WARNING_FONT_POINTS As Double = 9#

Private Const HEADER_FONT_POINTS As Double = 8.5
Private Const DATA_FONT_POINTS As Double = 10#
Private Const CUTLIST_DATA_FONT_POINTS As Double = 8#


' ============================================================
' POSITION
' ============================================================

Private Const START_MARGIN_X As Double = 0.02
Private Const START_MARGIN_Y As Double = 0.02

Private Const TABLE_GAP_X As Double = 0.012
Private Const TABLE_GAP_Y As Double = 0.012


' ============================================================
' DIAGRAM
' ============================================================

Private Const PROFILE_LENGTH As Long = 22
Private Const PROFILE_LEFT_MARGIN As Long = 4


' ============================================================
' GEOMETRY
' ============================================================

Private Const SIDE_DOT_LIMIT As Double = 0.12
Private Const END_DOT_MIN As Double = 0.15

Private Const CUT_AXIS_COS_LIMIT As Double = 0.98
Private Const PARALLEL_COS_LIMIT As Double = 0.985
Private Const ORTHOGONAL_DOT_LIMIT As Double = 0.3

Private Const END_ZONE_RATIO As Double = 0.35

Private Const H_EQUAL_TOL_MM As Double = 0.75
Private Const NOMINAL_SNAP_TOL_MM As Double = 1.5


' ============================================================
' MAIN
' ============================================================

Sub main()

    On Error GoTo ErrorHandler


    Dim swApp As Object
    Dim swModel As Object
    Dim swDraw As Object

    Dim swSelMgr As Object
    Dim swSourceTable As Object


    Set swApp = Application.SldWorks
    Set swModel = swApp.ActiveDoc


    If swModel Is Nothing Then

        MsgBox "Open a drawing first.", vbExclamation
        Exit Sub

    End If


    If swModel.GetType <> swDocDRAWING Then

        MsgBox _
            "This macro works only in a drawing.", _
            vbExclamation

        Exit Sub

    End If


    Set swDraw = swModel
    Set swSelMgr = swModel.SelectionManager


    If swSelMgr.GetSelectedObjectCount2(-1) = 0 Then

        MsgBox _
            "Select the required cells in the source table.", _
            vbExclamation

        Exit Sub

    End If


    Set swSourceTable = _
        swSelMgr.GetSelectedObject6(1, -1)


    If swSourceTable Is Nothing Then

        MsgBox _
            "Could not get the selected table.", _
            vbCritical

        Exit Sub

    End If


    ' ========================================================
    ' SELECTED RANGE
    ' ========================================================

    Dim firstRow As Long
    Dim lastRow As Long

    Dim firstCol As Long
    Dim lastCol As Long


    firstRow = -1
    lastRow = -1

    firstCol = -1
    lastCol = -1


    swSourceTable.GetCellRange _
        firstRow, _
        lastRow, _
        firstCol, _
        lastCol


    If firstRow < 0 Or _
       lastRow < 0 Or _
       firstCol < 0 Or _
       lastCol < 0 Then

        MsgBox _
            "Select cells inside the table.", _
            vbExclamation

        Exit Sub

    End If


    ' ========================================================
    ' REFERENCED PART
    ' ========================================================

    Dim swPart As Object

    Dim refConfig As String
    Dim oldConfig As String


    Set swPart = _
        GetReferencedPart( _
            swApp, _
            swDraw, _
            refConfig)


    If swPart Is Nothing Then

        MsgBox _
            "Could not get the part referenced by the drawing.", _
            vbCritical

        Exit Sub

    End If


    On Error Resume Next


    oldConfig = _
        swPart.ConfigurationManager.ActiveConfiguration.Name


    If refConfig <> "" Then

        swPart.ShowConfiguration2 _
            refConfig

    End If


    On Error GoTo ErrorHandler


    ' ========================================================
    ' SOURCE COLUMNS
    ' ========================================================

    Dim colName As Long
    Dim colMaterial As Long
    Dim colQty As Long

    Dim colHeight As Long
    Dim colWidth As Long

    Dim colDir1 As Long
    Dim colAngle1 As Long

    Dim colLength As Long

    Dim colAngle2 As Long
    Dim colDir2 As Long


    colName = -1
    colMaterial = -1
    colQty = -1

    colHeight = -1
    colWidth = -1

    colDir1 = -1
    colAngle1 = -1

    colLength = -1

    colAngle2 = -1
    colDir2 = -1


    Dim columnsOK As Boolean


    columnsOK = _
        DetectSourceColumns( _
            swSourceTable, _
            firstRow, _
            firstCol, _
            lastCol, _
            colName, _
            colMaterial, _
            colQty, _
            colHeight, _
            colWidth, _
            colDir1, _
            colAngle1, _
            colLength, _
            colAngle2, _
            colDir2)


    ' ========================================================
    ' FALLBACK
    ' ========================================================

    If Not columnsOK Then


        Dim selectedCols As Long


        selectedCols = _
            lastCol - firstCol + 1


        If selectedCols = 10 Then


            colName = firstCol
            colMaterial = firstCol + 1
            colQty = firstCol + 2

            colHeight = firstCol + 3
            colWidth = firstCol + 4

            colDir1 = firstCol + 5
            colAngle1 = firstCol + 6

            colLength = firstCol + 7

            colAngle2 = firstCol + 8
            colDir2 = firstCol + 9


        ElseIf selectedCols = 9 Then


            colName = -1

            colMaterial = firstCol
            colQty = firstCol + 1

            colHeight = firstCol + 2
            colWidth = firstCol + 3

            colDir1 = firstCol + 4
            colAngle1 = firstCol + 5

            colLength = firstCol + 6

            colAngle2 = firstCol + 7
            colDir2 = firstCol + 8


        Else


            MsgBox _
                "Could not determine source columns.", _
                vbCritical

            GoTo RestoreConfiguration


        End If


    End If


    ' ========================================================
    ' HEADER
    ' ========================================================

    Dim dataFirstRow As Long


    dataFirstRow = firstRow


    If RowLooksLikeHeader( _
            swSourceTable, _
            firstRow, _
            firstCol, _
            lastCol) Then


        dataFirstRow = _
            firstRow + 1


    End If


    ' ========================================================
    ' ARRAYS
    ' ========================================================

    Dim maxRows As Long


    maxRows = _
        lastRow - dataFirstRow + 1


    Dim groupMaterial() As String
    Dim groupNumbers() As String

    Dim groupH() As String
    Dim groupSeeDrawing() As Boolean

    Dim groupDir1() As String
    Dim groupAngle1() As String

    Dim groupAngle2() As String
    Dim groupDir2() As String


    ReDim groupMaterial(1 To maxRows)
    ReDim groupNumbers(1 To maxRows)

    ReDim groupH(1 To maxRows)
    ReDim groupSeeDrawing(1 To maxRows)

    ReDim groupDir1(1 To maxRows)
    ReDim groupAngle1(1 To maxRows)

    ReDim groupAngle2(1 To maxRows)
    ReDim groupDir2(1 To maxRows)


    Dim rowGroup() As Long

    Dim rowCutListName() As String
    Dim rowLength() As String
    Dim rowQty() As String


    ReDim rowGroup(1 To maxRows)

    ReDim rowCutListName(1 To maxRows)
    ReDim rowLength(1 To maxRows)
    ReDim rowQty(1 To maxRows)


    Dim groupCount As Long
    Dim validRowCount As Long


    groupCount = 0
    validRowCount = 0


    ' ========================================================
    ' READ SOURCE
    ' ========================================================

    Dim r As Long
    Dim g As Long


    Dim nameText As String
    Dim shortNumber As String

    Dim materialText As String
    Dim qtyText As String

    Dim heightText As String
    Dim widthText As String

    Dim dir1Text As String
    Dim angle1Text As String

    Dim lengthText As String

    Dim angle2Text As String
    Dim dir2Text As String


    Dim calculatedH As String
    Dim needsDrawing As Boolean

    Dim swBody As Object


    For r = dataFirstRow To lastRow


        If colName >= 0 Then


            nameText = _
                CleanText( _
                    GetTableText( _
                        swSourceTable, _
                        r, _
                        colName))


            shortNumber = _
                ExtractCutListNumber( _
                    nameText)


        Else


            nameText = "-"
            shortNumber = ""


        End If


        materialText = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colMaterial))


        qtyText = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colQty))


        If colHeight >= 0 Then


            heightText = _
                CleanText( _
                    GetTableText( _
                        swSourceTable, _
                        r, _
                        colHeight))


        Else


            heightText = ""


        End If


        If colWidth >= 0 Then


            widthText = _
                CleanText( _
                    GetTableText( _
                        swSourceTable, _
                        r, _
                        colWidth))


        Else


            widthText = ""


        End If


        dir1Text = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colDir1))


        angle1Text = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colAngle1))


        lengthText = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colLength))


        angle2Text = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colAngle2))


        dir2Text = _
            CleanText( _
                GetTableText( _
                    swSourceTable, _
                    r, _
                    colDir2))


        If lengthText <> "" Or qtyText <> "" Then


            calculatedH = ""
            needsDrawing = False


            ' ------------------------------------------------
            ' OUT OF PLANE
            ' ------------------------------------------------

            If IsOutOfPlaneGroup( _
                    dir1Text, _
                    dir2Text) Then


                needsDrawing = True


            Else


                Set swBody = Nothing


                Set swBody = _
                    FindCutListBody( _
                        swPart, _
                        nameText)


                If swBody Is Nothing Then


                    needsDrawing = True


                Else


                    If Not DetermineCutHeight( _
                            swBody, _
                            heightText, _
                            widthText, _
                            calculatedH) Then


                        needsDrawing = True


                    End If


                End If


            End If


            ' =================================================
            ' GROUP
            ' =================================================

            g = FindGroup( _
                    groupCount, _
                    groupMaterial, _
                    groupH, _
                    groupSeeDrawing, _
                    groupDir1, _
                    groupAngle1, _
                    groupAngle2, _
                    groupDir2, _
                    materialText, _
                    calculatedH, _
                    needsDrawing, _
                    dir1Text, _
                    angle1Text, _
                    angle2Text, _
                    dir2Text)


            If g = 0 Then


                groupCount = _
                    groupCount + 1


                g = groupCount


                groupMaterial(g) = _
                    materialText


                groupH(g) = _
                    calculatedH


                groupSeeDrawing(g) = _
                    needsDrawing


                groupDir1(g) = _
                    dir1Text


                groupAngle1(g) = _
                    angle1Text


                groupAngle2(g) = _
                    angle2Text


                groupDir2(g) = _
                    dir2Text


            End If


            groupNumbers(g) = _
                AppendUniqueNumber( _
                    groupNumbers(g), _
                    shortNumber)


            validRowCount = _
                validRowCount + 1


            rowGroup(validRowCount) = g

            rowCutListName(validRowCount) = _
                nameText

            rowLength(validRowCount) = _
                lengthText

            rowQty(validRowCount) = _
                qtyText


        End If


    Next r


    ' ========================================================
    ' ORDER
    ' ========================================================

    Dim outputOrder() As Long


    BuildOutputOrder _
        groupCount, _
        groupMaterial, _
        groupAngle1, _
        groupAngle2, _
        outputOrder


    ' ========================================================
    ' SHEET
    ' ========================================================

    Dim swSheet As Object

    Dim sheetWidth As Double
    Dim sheetHeight As Double


    Set swSheet = _
        swDraw.GetCurrentSheet


    swSheet.GetSize _
        sheetWidth, _
        sheetHeight


    Dim currentX As Double
    Dim currentY As Double
    Dim startY As Double


    currentX = START_MARGIN_X

    startY = _
        sheetHeight - START_MARGIN_Y

    currentY = startY


    ' ========================================================
    ' CREATE TABLES
    ' ========================================================

    Dim outputIndex As Long

    Dim dataCount As Long
    Dim tableHeight As Double


    For outputIndex = 1 To groupCount


        g = _
            outputOrder(outputIndex)


        dataCount = _
            CountRowsInGroup( _
                validRowCount, _
                rowGroup, _
                g)


        tableHeight = _
            ROW_NUMBER_HEIGHT + _
            ROW_MATERIAL_HEIGHT + _
            ROW_PROFILE_HEIGHT + _
            ROW_HEADER_HEIGHT + _
            ROW_DATA_HEIGHT * dataCount


        If groupSeeDrawing(g) Then


            tableHeight = _
                tableHeight + _
                ROW_WARNING_HEIGHT


        End If


        If currentY - tableHeight < 0.01 Then


            currentX = _
                currentX + _
                TABLE_TOTAL_WIDTH + _
                TABLE_GAP_X


            currentY = startY


        End If


        CreateProductionTable _
            swDraw, _
            currentX, _
            currentY, _
            groupNumbers(g), _
            groupMaterial(g), _
            groupH(g), _
            groupSeeDrawing(g), _
            groupDir1(g), _
            groupAngle1(g), _
            groupAngle2(g), _
            groupDir2(g), _
            g, _
            validRowCount, _
            rowGroup, _
            rowCutListName, _
            rowLength, _
            rowQty


        currentY = _
            currentY - _
            tableHeight - _
            TABLE_GAP_Y


    Next outputIndex


    swModel.ClearSelection2 True

    swModel.ForceRebuild3 False
    swModel.GraphicsRedraw2
    swModel.WindowRedraw


RestoreConfiguration:

    On Error Resume Next


    If oldConfig <> "" Then

        swPart.ShowConfiguration2 _
            oldConfig

    End If


    On Error GoTo 0


    Exit Sub


ErrorHandler:

    MsgBox _
        "Macro error" & vbCrLf & vbCrLf & _
        "Number: " & CStr(Err.Number) & vbCrLf & _
        "Description: " & Err.Description, _
        vbCritical

End Sub


' ============================================================
' DETERMINE H
' ============================================================

Private Function DetermineCutHeight( _
                ByVal swBody As Object, _
                ByVal sourceHeight As String, _
                ByVal sourceWidth As String, _
                ByRef hText As String) As Boolean


    On Error GoTo Failed


    hText = ""


    Dim longAxis As Variant


    longAxis = _
        GetLongAxis(swBody)


    If IsEmpty(longAxis) Then
        GoTo Failed
    End If


    Dim sectionAxis1 As Variant
    Dim sectionAxis2 As Variant

    Dim dim1 As Double
    Dim dim2 As Double


    If Not GetCrossSectionAxes( _
            swBody, _
            longAxis, _
            sectionAxis1, _
            dim1, _
            sectionAxis2, _
            dim2) Then

        GoTo Failed

    End If


    Dim endNormal1 As Variant
    Dim endNormal2 As Variant


    If Not GetEndPlaneNormals( _
            swBody, _
            longAxis, _
            endNormal1, _
            endNormal2) Then

        GoTo Failed

    End If


    Dim h1 As Double
    Dim h2 As Double

    Dim straight1 As Boolean
    Dim straight2 As Boolean

    Dim complex1 As Boolean
    Dim complex2 As Boolean


    ClassifyEndCut _
        endNormal1, _
        longAxis, _
        sectionAxis1, _
        dim1, _
        sectionAxis2, _
        dim2, _
        h1, _
        straight1, _
        complex1


    ClassifyEndCut _
        endNormal2, _
        longAxis, _
        sectionAxis1, _
        dim1, _
        sectionAxis2, _
        dim2, _
        h2, _
        straight2, _
        complex2


    If complex1 Or complex2 Then
        GoTo Failed
    End If


    If h1 > 0# Then

        h1 = h1 * 1000#

        h1 = _
            SnapToNominalDimension( _
                h1, _
                sourceHeight, _
                sourceWidth)

    End If


    If h2 > 0# Then

        h2 = h2 * 1000#

        h2 = _
            SnapToNominalDimension( _
                h2, _
                sourceHeight, _
                sourceWidth)

    End If


    Dim cutSide As Double


    cutSide = 0#


    ' ========================================================
    ' FIND CUT-SIDE DIMENSION
    ' ========================================================

    If straight1 And straight2 Then


        cutSide = _
            NumericTextValue(sourceWidth)


        If cutSide <= 0# Then

            cutSide = _
                NumericTextValue(sourceHeight)

        End If


    ElseIf straight1 And Not straight2 Then


        cutSide = h2


    ElseIf straight2 And Not straight1 Then


        cutSide = h1


    Else


        If h1 <= 0# Or h2 <= 0# Then
            GoTo Failed
        End If


        If Abs(h1 - h2) > H_EQUAL_TOL_MM Then

            GoTo Failed

        End If


        cutSide = _
            (h1 + h2) / 2#


    End If


    If cutSide <= 0# Then
        GoTo Failed
    End If


    ' ========================================================
    ' IMPORTANT CHANGE
    '
    ' Previous macro displayed the cut-side dimension.
    '
    ' NOW:
    '
    ' CUT SIDE 40 -> H 20
    ' CUT SIDE 20 -> H 40
    '
    ' H = actual height above the flat table.
    ' ========================================================

    Dim lyingHeight As Double


    lyingHeight = _
        SwapToLyingHeight( _
            cutSide, _
            sourceHeight, _
            sourceWidth)


    If lyingHeight <= 0# Then
        GoTo Failed
    End If


    hText = _
        "H - " & _
        FormatDimensionMM(lyingHeight)


    DetermineCutHeight = True

    Exit Function


Failed:

    hText = ""

    DetermineCutHeight = False

End Function


' ============================================================
' SWAP CUT SIDE -> REAL LYING HEIGHT
'
' Example:
'
' sourceHeight = 20
' sourceWidth  = 40
'
' cutSide = 40 -> H = 20
' cutSide = 20 -> H = 40
'
' 15 x 15:
'
' 15 -> 15
' ============================================================

Private Function SwapToLyingHeight( _
                ByVal cutSideMM As Double, _
                ByVal sourceHeight As String, _
                ByVal sourceWidth As String) As Double


    Dim nominalH As Double
    Dim nominalW As Double


    nominalH = _
        NumericTextValue(sourceHeight)


    nominalW = _
        NumericTextValue(sourceWidth)


    ' No nominal data available.

    If nominalH <= 0# And _
       nominalW <= 0# Then


        SwapToLyingHeight = _
            cutSideMM


        Exit Function


    End If


    ' Square profile.

    If nominalH > 0# And _
       nominalW > 0# Then


        If Abs(nominalH - nominalW) <= _
           H_EQUAL_TOL_MM Then


            SwapToLyingHeight = _
                nominalH


            Exit Function


        End If


    End If


    ' Cut is along HEIGHT dimension:
    ' lying height becomes WIDTH.

    If nominalH > 0# Then


        If Abs(cutSideMM - nominalH) <= _
           NOMINAL_SNAP_TOL_MM Then


            If nominalW > 0# Then

                SwapToLyingHeight = _
                    nominalW

            Else

                SwapToLyingHeight = _
                    nominalH

            End If


            Exit Function


        End If


    End If


    ' Cut is along WIDTH dimension:
    ' lying height becomes HEIGHT.

    If nominalW > 0# Then


        If Abs(cutSideMM - nominalW) <= _
           NOMINAL_SNAP_TOL_MM Then


            If nominalH > 0# Then

                SwapToLyingHeight = _
                    nominalH

            Else

                SwapToLyingHeight = _
                    nominalW

            End If


            Exit Function


        End If


    End If


    ' Unknown / calculated dimension.
    ' Do not invent another value.

    SwapToLyingHeight = _
        cutSideMM

End Function


' ============================================================
' CLASSIFY ONE CUT
'
' Returns dimension of the side in which the cut is made.
' The final H is swapped later.
' ============================================================

Private Sub ClassifyEndCut( _
                ByVal cutNormal As Variant, _
                ByVal longAxis As Variant, _
                ByVal axis1 As Variant, _
                ByVal dim1 As Double, _
                ByVal axis2 As Variant, _
                ByVal dim2 As Double, _
                ByRef hValue As Double, _
                ByRef isStraight As Boolean, _
                ByRef isComplex As Boolean)


    hValue = 0#

    isStraight = False
    isComplex = False


    Dim axialComponent As Double


    axialComponent = _
        DotProduct( _
            cutNormal, _
            longAxis)


    Dim tx As Double
    Dim ty As Double
    Dim tz As Double


    tx = _
        cutNormal(0) - _
        axialComponent * longAxis(0)


    ty = _
        cutNormal(1) - _
        axialComponent * longAxis(1)


    tz = _
        cutNormal(2) - _
        axialComponent * longAxis(2)


    Dim transverseMagnitude As Double


    transverseMagnitude = _
        Sqr( _
            tx * tx + _
            ty * ty + _
            tz * tz)


    ' Straight cut.

    If transverseMagnitude < 0.01 Then


        isStraight = True

        Exit Sub


    End If


    Dim transverse As Variant


    transverse = _
        NormalizeVector( _
            Array( _
                tx, _
                ty, _
                tz))


    Dim dot1 As Double
    Dim dot2 As Double


    dot1 = _
        Abs( _
            DotProduct( _
                transverse, _
                axis1))


    dot2 = _
        Abs( _
            DotProduct( _
                transverse, _
                axis2))


    If dot1 < CUT_AXIS_COS_LIMIT And _
       dot2 < CUT_AXIS_COS_LIMIT Then


        isComplex = True

        Exit Sub


    End If


    If dot1 >= dot2 Then

        hValue = dim1

    Else

        hValue = dim2

    End If

End Sub


' ============================================================
' LONG AXIS
' ============================================================

Private Function GetLongAxis( _
                ByVal swBody As Object) As Variant


    On Error GoTo Failed


    Dim vEdges As Variant


    vEdges = _
        swBody.GetEdges


    If IsEmpty(vEdges) Then
        GoTo Failed
    End If


    Dim vE As Variant

    Dim swEdge As Object
    Dim swV1 As Object
    Dim swV2 As Object

    Dim p1 As Variant
    Dim p2 As Variant


    Dim dx As Double
    Dim dy As Double
    Dim dz As Double

    Dim lengthValue As Double
    Dim maxLength As Double


    Dim bestX As Double
    Dim bestY As Double
    Dim bestZ As Double


    maxLength = 0#


    For Each vE In vEdges


        Set swEdge = vE

        Set swV1 = Nothing
        Set swV2 = Nothing


        Set swV1 = _
            swEdge.GetStartVertex


        Set swV2 = _
            swEdge.GetEndVertex


        If Not swV1 Is Nothing And _
           Not swV2 Is Nothing Then


            p1 = swV1.GetPoint
            p2 = swV2.GetPoint


            dx = _
                CDbl(p2(0)) - CDbl(p1(0))


            dy = _
                CDbl(p2(1)) - CDbl(p1(1))


            dz = _
                CDbl(p2(2)) - CDbl(p1(2))


            lengthValue = _
                Sqr( _
                    dx * dx + _
                    dy * dy + _
                    dz * dz)


            If lengthValue > maxLength Then


                maxLength = lengthValue

                bestX = dx
                bestY = dy
                bestZ = dz


            End If


        End If


    Next vE


    If maxLength <= 0# Then
        GoTo Failed
    End If


    GetLongAxis = _
        NormalizeVector( _
            Array( _
                bestX, _
                bestY, _
                bestZ))


    Exit Function


Failed:

    GetLongAxis = Empty

End Function


' ============================================================
' CROSS SECTION AXES
' ============================================================

Private Function GetCrossSectionAxes( _
                ByVal swBody As Object, _
                ByVal longAxis As Variant, _
                ByRef axis1 As Variant, _
                ByRef dim1 As Double, _
                ByRef axis2 As Variant, _
                ByRef dim2 As Double) As Boolean


    On Error GoTo Failed


    Dim vFaces As Variant


    vFaces = _
        swBody.GetFaces


    If IsEmpty(vFaces) Then
        GoTo Failed
    End If


    Dim nx(1 To 60) As Double
    Dim ny(1 To 60) As Double
    Dim nz(1 To 60) As Double

    Dim areaSum(1 To 60) As Double


    Dim directionCount As Long


    directionCount = 0


    Dim vF As Variant

    Dim swFace As Object
    Dim swSurf As Object

    Dim params As Variant
    Dim n As Variant

    Dim axialDot As Double
    Dim faceArea As Double

    Dim i As Long
    Dim groupIndex As Long

    Dim foundParallel As Boolean


    For Each vF In vFaces


        Set swFace = vF

        Set swSurf = _
            swFace.GetSurface


        If Not swSurf Is Nothing Then


            If swSurf.IsPlane Then


                params = _
                    swSurf.PlaneParams


                n = _
                    NormalizeVector( _
                        Array( _
                            CDbl(params(0)), _
                            CDbl(params(1)), _
                            CDbl(params(2))))


                axialDot = _
                    Abs( _
                        DotProduct( _
                            n, _
                            longAxis))


                If axialDot < SIDE_DOT_LIMIT Then


                    faceArea = _
                        swFace.GetArea


                    foundParallel = False


                    For i = 1 To directionCount


                        If Abs( _
                                n(0) * nx(i) + _
                                n(1) * ny(i) + _
                                n(2) * nz(i)) >= _
                           PARALLEL_COS_LIMIT Then


                            foundParallel = True

                            groupIndex = i

                            Exit For


                        End If


                    Next i


                    If foundParallel Then


                        areaSum(groupIndex) = _
                            areaSum(groupIndex) + _
                            faceArea


                    Else


                        directionCount = _
                            directionCount + 1


                        nx(directionCount) = n(0)
                        ny(directionCount) = n(1)
                        nz(directionCount) = n(2)


                        areaSum(directionCount) = _
                            faceArea


                    End If


                End If


            End If


        End If


    Next vF


    If directionCount < 2 Then
        GoTo Failed
    End If


    Dim j As Long

    Dim pairDot As Double
    Dim pairScore As Double

    Dim bestScore As Double

    Dim bestI As Long
    Dim bestJ As Long


    bestScore = -1#


    For i = 1 To directionCount - 1


        For j = i + 1 To directionCount


            pairDot = _
                Abs( _
                    nx(i) * nx(j) + _
                    ny(i) * ny(j) + _
                    nz(i) * nz(j))


            If pairDot <= ORTHOGONAL_DOT_LIMIT Then


                pairScore = _
                    areaSum(i) + _
                    areaSum(j)


                If pairScore > bestScore Then


                    bestScore = pairScore

                    bestI = i
                    bestJ = j


                End If


            End If


        Next j


    Next i


    If bestI = 0 Or bestJ = 0 Then
        GoTo Failed
    End If


    axis1 = _
        Array( _
            nx(bestI), _
            ny(bestI), _
            nz(bestI))


    axis2 = _
        Array( _
            nx(bestJ), _
            ny(bestJ), _
            nz(bestJ))


    dim1 = _
        ProjectionExtent( _
            swBody, _
            axis1)


    dim2 = _
        ProjectionExtent( _
            swBody, _
            axis2)


    If dim1 <= 0# Or dim2 <= 0# Then
        GoTo Failed
    End If


    GetCrossSectionAxes = True

    Exit Function


Failed:

    GetCrossSectionAxes = False

End Function


' ============================================================
' END NORMALS
' ============================================================

Private Function GetEndPlaneNormals( _
                ByVal swBody As Object, _
                ByVal longAxis As Variant, _
                ByRef normal1 As Variant, _
                ByRef normal2 As Variant) As Boolean


    On Error GoTo Failed


    Dim bodyMin As Double
    Dim bodyMax As Double


    If Not ProjectionMinMax( _
            swBody, _
            longAxis, _
            bodyMin, _
            bodyMax) Then

        GoTo Failed

    End If


    Dim bodyLength As Double


    bodyLength = _
        bodyMax - bodyMin


    Dim vFaces As Variant


    vFaces = _
        swBody.GetFaces


    If IsEmpty(vFaces) Then
        GoTo Failed
    End If


    Dim bestArea1 As Double
    Dim bestArea2 As Double


    Dim vF As Variant

    Dim swFace As Object
    Dim swSurf As Object

    Dim params As Variant
    Dim n As Variant

    Dim axialDot As Double

    Dim box As Variant

    Dim cx As Double
    Dim cy As Double
    Dim cz As Double

    Dim t As Double
    Dim faceArea As Double


    For Each vF In vFaces


        Set swFace = vF

        Set swSurf = _
            swFace.GetSurface


        If Not swSurf Is Nothing Then


            If swSurf.IsPlane Then


                params = _
                    swSurf.PlaneParams


                n = _
                    NormalizeVector( _
                        Array( _
                            CDbl(params(0)), _
                            CDbl(params(1)), _
                            CDbl(params(2))))


                axialDot = _
                    Abs( _
                        DotProduct( _
                            n, _
                            longAxis))


                If axialDot > END_DOT_MIN Then


                    box = _
                        swFace.GetBox


                    cx = _
                        (CDbl(box(0)) + CDbl(box(3))) / 2#


                    cy = _
                        (CDbl(box(1)) + CDbl(box(4))) / 2#


                    cz = _
                        (CDbl(box(2)) + CDbl(box(5))) / 2#


                    t = _
                        cx * longAxis(0) + _
                        cy * longAxis(1) + _
                        cz * longAxis(2)


                    faceArea = _
                        swFace.GetArea


                    If t <= _
                       bodyMin + _
                       bodyLength * END_ZONE_RATIO Then


                        If faceArea > bestArea1 Then


                            bestArea1 = faceArea

                            normal1 = n


                        End If


                    End If


                    If t >= _
                       bodyMax - _
                       bodyLength * END_ZONE_RATIO Then


                        If faceArea > bestArea2 Then


                            bestArea2 = faceArea

                            normal2 = n


                        End If


                    End If


                End If


            End If


        End If


    Next vF


    If bestArea1 <= 0# Or bestArea2 <= 0# Then
        GoTo Failed
    End If


    GetEndPlaneNormals = True

    Exit Function


Failed:

    GetEndPlaneNormals = False

End Function


' ============================================================
' BODY PROJECTION
' ============================================================

Private Function ProjectionExtent( _
                ByVal swBody As Object, _
                ByVal axis As Variant) As Double


    Dim minValue As Double
    Dim maxValue As Double


    If ProjectionMinMax( _
            swBody, _
            axis, _
            minValue, _
            maxValue) Then


        ProjectionExtent = _
            maxValue - minValue


    End If

End Function


Private Function ProjectionMinMax( _
                ByVal swBody As Object, _
                ByVal axis As Variant, _
                ByRef minValue As Double, _
                ByRef maxValue As Double) As Boolean


    On Error GoTo Failed


    Dim vVertices As Variant


    vVertices = _
        swBody.GetVertices


    If IsEmpty(vVertices) Then
        GoTo Failed
    End If


    Dim firstPoint As Boolean


    firstPoint = True


    Dim vV As Variant
    Dim swVertex As Object

    Dim p As Variant

    Dim projection As Double


    For Each vV In vVertices


        Set swVertex = vV


        p = swVertex.GetPoint


        projection = _
            CDbl(p(0)) * axis(0) + _
            CDbl(p(1)) * axis(1) + _
            CDbl(p(2)) * axis(2)


        If firstPoint Then


            minValue = projection
            maxValue = projection

            firstPoint = False


        Else


            If projection < minValue Then
                minValue = projection
            End If


            If projection > maxValue Then
                maxValue = projection
            End If


        End If


    Next vV


    ProjectionMinMax = _
        Not firstPoint


    Exit Function


Failed:

    ProjectionMinMax = False

End Function


' ============================================================
' REFERENCED PART
' ============================================================

Private Function GetReferencedPart( _
                ByVal swApp As Object, _
                ByVal swDraw As Object, _
                ByRef configName As String) As Object


    On Error Resume Next


    Dim swView As Object
    Dim swRef As Object


    Set swView = _
        swDraw.GetFirstView


    Set swView = _
        swView.GetNextView


    Do While Not swView Is Nothing


        Set swRef = _
            swView.ReferencedDocument


        If Not swRef Is Nothing Then


            If swRef.GetType = swDocPART Then


                configName = _
                    swView.ReferencedConfiguration


                Set GetReferencedPart = swRef

                Exit Function


            End If


        End If


        Set swView = _
            swView.GetNextView


    Loop

End Function


' ============================================================
' CUT LIST BODY
' ============================================================

Private Function FindCutListBody( _
                ByVal swPart As Object, _
                ByVal wantedName As String) As Object


    Dim swFeat As Object


    Set swFeat = _
        swPart.FirstFeature


    Do While Not swFeat Is Nothing


        Set FindCutListBody = _
            FindCutListBodyRecursive( _
                swFeat, _
                wantedName)


        If Not FindCutListBody Is Nothing Then
            Exit Function
        End If


        Set swFeat = _
            swFeat.GetNextFeature


    Loop

End Function


Private Function FindCutListBodyRecursive( _
                ByVal swFeat As Object, _
                ByVal wantedName As String) As Object


    If swFeat Is Nothing Then
        Exit Function
    End If


    Dim typeName As String


    On Error Resume Next

    typeName = _
        swFeat.GetTypeName2

    On Error GoTo 0


    If StrComp( _
            typeName, _
            "CutListFolder", _
            vbTextCompare) = 0 Then


        If CutListNameMatches( _
                swFeat.Name, _
                wantedName) Then


            Set FindCutListBodyRecursive = _
                GetFirstBodyFromCutListFeature( _
                    swFeat)


            Exit Function


        End If


    End If


    Dim swSub As Object


    Set swSub = _
        swFeat.GetFirstSubFeature


    Do While Not swSub Is Nothing


        Set FindCutListBodyRecursive = _
            FindCutListBodyRecursive( _
                swSub, _
                wantedName)


        If Not FindCutListBodyRecursive Is Nothing Then
            Exit Function
        End If


        Set swSub = _
            swSub.GetNextSubFeature


    Loop

End Function


Private Function GetFirstBodyFromCutListFeature( _
                ByVal swFeat As Object) As Object


    On Error Resume Next


    Dim folder As Object
    Dim bodies As Variant


    Set folder = _
        swFeat.GetSpecificFeature2


    bodies = _
        folder.GetBodies


    If Not IsEmpty(bodies) Then


        Set GetFirstBodyFromCutListFeature = _
            bodies(LBound(bodies))


    End If

End Function


Private Function CutListNameMatches( _
                ByVal featureName As String, _
                ByVal wantedName As String) As Boolean


    Dim f As String
    Dim w As String


    f = _
        CleanText(featureName)


    w = _
        ExtractCutListNumber(wantedName)


    If StrComp( _
            f, _
            w, _
            vbTextCompare) = 0 Then


        CutListNameMatches = True

        Exit Function


    End If


    Dim bracketValue As String


    bracketValue = _
        ExtractLastBracketValue(f)


    If bracketValue <> "" Then


        If StrComp( _
                bracketValue, _
                w, _
                vbTextCompare) = 0 Then


            CutListNameMatches = True

            Exit Function


        End If


    End If


    If IsNumeric(w) Then


        If Val(GetLeadingDigits(f)) = Val(w) Then


            CutListNameMatches = True


        End If


    End If

End Function


' ============================================================
' OUTPUT TABLE
' ============================================================

Private Sub CreateProductionTable( _
                ByVal swDraw As Object, _
                ByVal tableX As Double, _
                ByVal tableY As Double, _
                ByVal cutListNumbers As String, _
                ByVal materialName As String, _
                ByVal hText As String, _
                ByVal seePartDrawing As Boolean, _
                ByVal direction1 As String, _
                ByVal angle1 As String, _
                ByVal angle2 As String, _
                ByVal direction2 As String, _
                ByVal targetGroup As Long, _
                ByVal validRowCount As Long, _
                ByRef rowGroup() As Long, _
                ByRef rowCutListName() As String, _
                ByRef rowLength() As String, _
                ByRef rowQty() As String)


    Dim dataCount As Long


    dataCount = _
        CountRowsInGroup( _
            validRowCount, _
            rowGroup, _
            targetGroup)


    Dim rowNumber As Long
    Dim rowMaterial As Long
    Dim rowProfile As Long

    Dim rowWarning As Long
    Dim rowHeader As Long
    Dim rowDataStart As Long


    rowNumber = 0
    rowMaterial = 1
    rowProfile = 2


    If seePartDrawing Then


        rowWarning = 3
        rowHeader = 4
        rowDataStart = 5


    Else


        rowWarning = -1
        rowHeader = 3
        rowDataStart = 4


    End If


    Dim totalRows As Long


    totalRows = _
        rowDataStart + dataCount


    Dim swTable As Object


    Set swTable = _
        swDraw.InsertTableAnnotation2( _
            False, _
            tableX, _
            tableY, _
            TABLE_ANCHOR_TOPLEFT, _
            "", _
            totalRows, _
            3)


    swTable.SetColumnWidth _
        0, _
        COL_CUTLIST_WIDTH, _
        0


    swTable.SetColumnWidth _
        1, _
        COL_LENGTH_WIDTH, _
        0


    swTable.SetColumnWidth _
        2, _
        COL_QTY_WIDTH, _
        0


    ApplyTableLines swTable


    ' NUMBER

    swTable.MergeCells _
        rowNumber, _
        0, _
        rowNumber, _
        2


    swTable.Text(rowNumber, 0) = _
        ChrW(8470) & _
        " " & _
        cutListNumbers


    ApplyCellFormat _
        swTable, _
        rowNumber, _
        0, _
        "Arial", _
        NUMBER_FONT_POINTS, _
        True, _
        1#


    ' MATERIAL

    swTable.MergeCells _
        rowMaterial, _
        0, _
        rowMaterial, _
        2


    swTable.Text(rowMaterial, 0) = _
        materialName


    ApplyCellFormat _
        swTable, _
        rowMaterial, _
        0, _
        "Arial", _
        MATERIAL_FONT_POINTS, _
        True, _
        1#


    ' PROFILE

    swTable.MergeCells _
        rowProfile, _
        0, _
        rowProfile, _
        2


    swTable.Text(rowProfile, 0) = _
        BuildAngleDiagram( _
            "PROFILE ANGLE", _
            IIf(seePartDrawing, "", hText), _
            direction1, _
            angle1, _
            angle2, _
            direction2)


    ApplyDiagramTextFormat _
        swTable, _
        rowProfile, _
        0


    ' WARNING

    If seePartDrawing Then


        swTable.MergeCells _
            rowWarning, _
            0, _
            rowWarning, _
            2


        swTable.Text(rowWarning, 0) = _
            "SEE PART DRAWING"


        ApplyCellFormat _
            swTable, _
            rowWarning, _
            0, _
            "Arial", _
            WARNING_FONT_POINTS, _
            True, _
            1#


    End If


    ' HEADER

    swTable.Text(rowHeader, 0) = _
        "CUT LIST"


    swTable.Text(rowHeader, 1) = _
        "LENGTH"


    swTable.Text(rowHeader, 2) = _
        "QTY"


    Dim c As Long


    For c = 0 To 2


        ApplyCellFormat _
            swTable, _
            rowHeader, _
            c, _
            "Arial", _
            HEADER_FONT_POINTS, _
            True, _
            1#


    Next c


    ' DATA

    Dim sortedRows() As Long


    ReDim sortedRows(1 To dataCount)


    Dim n As Long
    Dim i As Long


    For i = 1 To validRowCount


        If rowGroup(i) = targetGroup Then


            n = n + 1


            sortedRows(n) = i


        End If


    Next i


    SortRowsByLengthDescending _
        sortedRows, _
        dataCount, _
        rowLength


    Dim outputRow As Long
    Dim actualRow As Long


    outputRow = rowDataStart


    For i = 1 To dataCount


        actualRow = _
            sortedRows(i)


        swTable.Text(outputRow, 0) = _
            rowCutListName(actualRow)


        swTable.Text(outputRow, 1) = _
            rowLength(actualRow)


        swTable.Text(outputRow, 2) = _
            rowQty(actualRow)


        ApplyCellFormat _
            swTable, _
            outputRow, _
            0, _
            "Arial", _
            CUTLIST_DATA_FONT_POINTS, _
            False, _
            1#


        ApplyCellFormat _
            swTable, _
            outputRow, _
            1, _
            "Arial", _
            DATA_FONT_POINTS, _
            True, _
            1#


        ApplyCellFormat _
            swTable, _
            outputRow, _
            2, _
            "Arial", _
            DATA_FONT_POINTS, _
            True, _
            1#


        outputRow = _
            outputRow + 1


    Next i


    swTable.SetRowHeight _
        rowNumber, _
        ROW_NUMBER_HEIGHT, _
        0


    swTable.SetRowHeight _
        rowMaterial, _
        ROW_MATERIAL_HEIGHT, _
        0


    swTable.SetRowHeight _
        rowProfile, _
        ROW_PROFILE_HEIGHT, _
        0


    If seePartDrawing Then


        swTable.SetRowHeight _
            rowWarning, _
            ROW_WARNING_HEIGHT, _
            0


    End If


    swTable.SetRowHeight _
        rowHeader, _
        ROW_HEADER_HEIGHT, _
        0


    For i = rowDataStart To totalRows - 1


        swTable.SetRowHeight _
            i, _
            ROW_DATA_HEIGHT, _
            0


    Next i

End Sub


' ============================================================
' DIAGRAM
' ============================================================

Private Function BuildAngleDiagram( _
                ByVal title As String, _
                ByVal hText As String, _
                ByVal direction1 As String, _
                ByVal angle1Text As String, _
                ByVal angle2Text As String, _
                ByVal direction2 As String) As String


    Dim a1 As Double
    Dim a2 As Double


    a1 = _
        90# - AngleNumber(angle1Text)


    a2 = _
        90# - AngleNumber(angle2Text)


    Dim deg As String


    deg = ChrW(176)


    Dim leftLabel As String
    Dim rightLabel As String


    leftLabel = _
        FormatAngle(a1) & deg


    rightLabel = _
        FormatAngle(a2) & deg


    Dim HLINE As String
    Dim VLINE As String
    Dim SL As String
    Dim BS As String


    HLINE = ChrW(9473)
    VLINE = ChrW(9474)

    SL = ChrW(9585)
    BS = ChrW(9586)


    Dim leftEnd As String
    Dim rightEnd As String


    If AngleEquals( _
            AngleNumber(angle1Text), _
            0#) Then


        leftEnd = VLINE


    Else


        leftEnd = SL


    End If


    If AngleEquals( _
            AngleNumber(angle2Text), _
            0#) Then


        rightEnd = VLINE


    ElseIf IsSameDirection( _
                direction1, _
                direction2) Then


        rightEnd = SL


    Else


        rightEnd = BS


    End If


    Dim leftPos As Long
    Dim rightPos As Long
    Dim totalWidth As Long


    leftPos = _
        PROFILE_LEFT_MARGIN + 1


    rightPos = _
        leftPos + _
        PROFILE_LENGTH + 1


    totalWidth = _
        rightPos + 2


    Dim titleLine As String
    Dim angleLine As String
    Dim profileLine As String


    titleLine = _
        BuildProfileTitleLine( _
            title, _
            hText, _
            totalWidth)


    angleLine = _
        BuildAlignedAngleLine( _
            leftLabel, _
            rightLabel, _
            leftPos, _
            rightPos, _
            totalWidth)


    profileLine = _
        Space$(PROFILE_LEFT_MARGIN) & _
        leftEnd & _
        RepeatChar( _
            HLINE, _
            PROFILE_LENGTH) & _
        rightEnd


    BuildAngleDiagram = _
        titleLine & _
        vbCrLf & _
        angleLine & _
        vbCrLf & _
        profileLine

End Function


Private Function BuildProfileTitleLine( _
                ByVal title As String, _
                ByVal hText As String, _
                ByVal totalWidth As Long) As String


    Dim s As String


    s = Space$(totalWidth)


    If hText <> "" Then


        Mid$( _
            s, _
            1, _
            Len(hText)) = _
            hText


    End If


    Dim p As Long


    p = _
        ((totalWidth - Len(title)) \ 2) + 1


    If p <= Len(hText) + 1 Then


        p = _
            Len(hText) + 2


    End If


    Mid$( _
        s, _
        p, _
        Len(title)) = _
        title


    BuildProfileTitleLine = _
        RTrim$(s)

End Function


' ============================================================
' GROUP
' ============================================================

Private Function FindGroup( _
                ByVal groupCount As Long, _
                ByRef groupMaterial() As String, _
                ByRef groupH() As String, _
                ByRef groupSeeDrawing() As Boolean, _
                ByRef groupDir1() As String, _
                ByRef groupAngle1() As String, _
                ByRef groupAngle2() As String, _
                ByRef groupDir2() As String, _
                ByVal material As String, _
                ByVal hText As String, _
                ByVal seeDrawing As Boolean, _
                ByVal dir1 As String, _
                ByVal angle1 As String, _
                ByVal angle2 As String, _
                ByVal dir2 As String) As Long


    Dim i As Long


    For i = 1 To groupCount


        If SameText( _
                groupMaterial(i), _
                material) And _
           SameText( _
                groupH(i), _
                hText) And _
           groupSeeDrawing(i) = seeDrawing And _
           SameText( _
                groupDir1(i), _
                dir1) And _
           NormalizeAngle( _
                groupAngle1(i)) = _
                NormalizeAngle(angle1) And _
           NormalizeAngle( _
                groupAngle2(i)) = _
                NormalizeAngle(angle2) And _
           SameText( _
                groupDir2(i), _
                dir2) Then


            FindGroup = i

            Exit Function


        End If


    Next i

End Function


' ============================================================
' SORT TABLES
' ============================================================

Private Sub BuildOutputOrder( _
                ByVal groupCount As Long, _
                ByRef groupMaterial() As String, _
                ByRef groupAngle1() As String, _
                ByRef groupAngle2() As String, _
                ByRef outputOrder() As Long)


    ReDim outputOrder(1 To groupCount)


    Dim used() As Boolean


    ReDim used(1 To groupCount)


    Dim i As Long
    Dim p As Long

    Dim best As Long


    For p = 1 To groupCount


        best = 0


        For i = 1 To groupCount


            If Not used(i) Then


                If best = 0 Then


                    best = i


                ElseIf MinAngle( _
                            groupAngle1(i), _
                            groupAngle2(i)) < _
                       MinAngle( _
                            groupAngle1(best), _
                            groupAngle2(best)) Then


                    best = i


                End If


            End If


        Next i


        outputOrder(p) = best

        used(best) = True


    Next p

End Sub


' ============================================================
' LENGTH SORT
' ============================================================

Private Sub SortRowsByLengthDescending( _
                ByRef rows() As Long, _
                ByVal count As Long, _
                ByRef lengths() As String)


    Dim i As Long
    Dim j As Long
    Dim t As Long


    For i = 1 To count - 1


        For j = i + 1 To count


            If LengthNumber( _
                    lengths(rows(j))) > _
               LengthNumber( _
                    lengths(rows(i))) Then


                t = rows(i)

                rows(i) = rows(j)
                rows(j) = t


            End If


        Next j


    Next i

End Sub


' ============================================================
' OUT OF PLANE
' ============================================================

Private Function IsOutOfPlaneGroup( _
                ByVal dir1 As String, _
                ByVal dir2 As String) As Boolean


    IsOutOfPlaneGroup = _
        IsOutOfPlaneText(dir1) Or _
        IsOutOfPlaneText(dir2)

End Function


Private Function IsOutOfPlaneText( _
                ByVal value As String) As Boolean


    Dim s As String


    s = _
        UCase$( _
            CleanText(value))


    Dim ruOut As String
    Dim ruPlane As String


    ruOut = _
        ChrW(1042) & _
        ChrW(1053) & _
        ChrW(1045)


    ruPlane = _
        ChrW(1055) & _
        ChrW(1051) & _
        ChrW(1054) & _
        ChrW(1057) & _
        ChrW(1050)


    IsOutOfPlaneText = _
        (InStr(1, s, ruOut, vbTextCompare) > 0 And _
         InStr(1, s, ruPlane, vbTextCompare) > 0)

End Function


' ============================================================
' COLUMN DETECTION
' ============================================================

Private Function DetectSourceColumns( _
                ByVal swTable As Object, _
                ByVal headerRow As Long, _
                ByVal firstCol As Long, _
                ByVal lastCol As Long, _
                ByRef colName As Long, _
                ByRef colMaterial As Long, _
                ByRef colQty As Long, _
                ByRef colHeight As Long, _
                ByRef colWidth As Long, _
                ByRef colDir1 As Long, _
                ByRef colAngle1 As Long, _
                ByRef colLength As Long, _
                ByRef colAngle2 As Long, _
                ByRef colDir2 As Long) As Boolean


    Dim c As Long
    Dim h As String
    Dim dirCount As Long


    For c = firstCol To lastCol


        h = _
            NormalizeHeader( _
                GetTableText( _
                    swTable, _
                    headerRow, _
                    c))


        If h = "N" Or h = "NAME" Then


            colName = c


        ElseIf InStr(h, "MATERIAL") > 0 Or _
               IsRussianMaterialHeader(h) Then


            colMaterial = c


        ElseIf h = "QTY" Or _
               InStr(h, "QUANTITY") > 0 Or _
               IsRussianQtyHeader(h) Then


            colQty = c


        ElseIf InStr(h, "HEIGHT") > 0 Or _
               IsRussianHeightHeader(h) Then


            colHeight = c


        ElseIf InStr(h, "WIDTH") > 0 Or _
               IsRussianWidthHeader(h) Then


            colWidth = c


        ElseIf InStr(h, "ANGLE1") > 0 Or _
               IsRussianAngle1Header(h) Then


            colAngle1 = c


        ElseIf InStr(h, "ANGLE2") > 0 Or _
               IsRussianAngle2Header(h) Then


            colAngle2 = c


        ElseIf InStr(h, "LENGTH") > 0 Then


            colLength = c


        ElseIf IsRussianDirectionHeader(h) Then


            dirCount = dirCount + 1


            If dirCount = 1 Then

                colDir1 = c

            Else

                colDir2 = c

            End If


        End If


    Next c


    DetectSourceColumns = _
        (colMaterial >= 0 And _
         colQty >= 0 And _
         colDir1 >= 0 And _
         colAngle1 >= 0 And _
         colLength >= 0 And _
         colAngle2 >= 0 And _
         colDir2 >= 0)

End Function


' ============================================================
' HEADER HELPERS
' ============================================================

Private Function NormalizeHeader( _
                ByVal value As String) As String


    Dim s As String


    s = _
        UCase$( _
            CleanText(value))


    s = Replace(s, " ", "")
    s = Replace(s, "-", "")
    s = Replace(s, "_", "")
    s = Replace(s, ".", "")


    NormalizeHeader = s

End Function


Private Function RowLooksLikeHeader( _
                ByVal swTable As Object, _
                ByVal rowIndex As Long, _
                ByVal firstCol As Long, _
                ByVal lastCol As Long) As Boolean


    Dim c As Long
    Dim h As String


    For c = firstCol To lastCol


        h = _
            NormalizeHeader( _
                GetTableText( _
                    swTable, _
                    rowIndex, _
                    c))


        If InStr(h, "MATERIAL") > 0 Or _
           InStr(h, "LENGTH") > 0 Or _
           IsRussianMaterialHeader(h) Then


            RowLooksLikeHeader = True

            Exit Function


        End If


    Next c

End Function


Private Function IsRussianMaterialHeader( _
                ByVal s As String) As Boolean


    IsRussianMaterialHeader = _
        ContainsUnicodeWord( _
            s, _
            Array(1052, 1040, 1058, 1045, 1056, 1048, 1040, 1051))

End Function


Private Function IsRussianQtyHeader( _
                ByVal s As String) As Boolean


    IsRussianQtyHeader = _
        ContainsUnicodeWord( _
            s, _
            Array(1050, 1054, 1051))

End Function


Private Function IsRussianHeightHeader( _
                ByVal s As String) As Boolean


    IsRussianHeightHeader = _
        ContainsUnicodeWord( _
            s, _
            Array(1042, 1067, 1057, 1054, 1058))

End Function


Private Function IsRussianWidthHeader( _
                ByVal s As String) As Boolean


    IsRussianWidthHeader = _
        ContainsUnicodeWord( _
            s, _
            Array(1064, 1048, 1056, 1048, 1053))

End Function


Private Function IsRussianDirectionHeader( _
                ByVal s As String) As Boolean


    IsRussianDirectionHeader = _
        ContainsUnicodeWord( _
            s, _
            Array(1053, 1040, 1055, 1056, 1040, 1042, 1051, 1045, 1053))

End Function


Private Function IsRussianAngle1Header( _
                ByVal s As String) As Boolean


    IsRussianAngle1Header = _
        (InStr( _
            s, _
            UnicodeWord( _
                Array(1059, 1043, 1054, 1051)) & _
            "1") > 0)

End Function


Private Function IsRussianAngle2Header( _
                ByVal s As String) As Boolean


    IsRussianAngle2Header = _
        (InStr( _
            s, _
            UnicodeWord( _
                Array(1059, 1043, 1054, 1051)) & _
            "2") > 0)

End Function


Private Function ContainsUnicodeWord( _
                ByVal value As String, _
                ByVal codes As Variant) As Boolean


    ContainsUnicodeWord = _
        (InStr( _
            1, _
            value, _
            UnicodeWord(codes), _
            vbTextCompare) > 0)

End Function


Private Function UnicodeWord( _
                ByVal codes As Variant) As String


    Dim i As Long


    For i = LBound(codes) To UBound(codes)


        UnicodeWord = _
            UnicodeWord & _
            ChrW(codes(i))


    Next i

End Function


' ============================================================
' FORMAT
' ============================================================

Private Sub ApplyTableLines( _
                ByVal swTable As Object)


    On Error Resume Next


    swTable.BorderLineWeight = _
        swLW_NORMAL


    swTable.GridLineWeight = _
        swLW_THIN


    On Error GoTo 0

End Sub


Private Sub ApplyCellFormat( _
                ByVal swTable As Object, _
                ByVal r As Long, _
                ByVal c As Long, _
                ByVal fontName As String, _
                ByVal fontSize As Double, _
                ByVal isBold As Boolean, _
                ByVal spacing As Double)


    On Error Resume Next


    Dim tf As Object


    Set tf = _
        swTable.GetCellTextFormat( _
            r, _
            c)


    tf.TypeFaceName = fontName
    tf.CharHeightInPts = fontSize
    tf.CharSpacingFactor = spacing
    tf.Bold = isBold


    swTable.SetCellTextFormat _
        r, _
        c, _
        False, _
        tf


    swTable.CellTextHorizontalJustification( _
        r, _
        c) = _
        swTextJustificationCenter


    On Error GoTo 0

End Sub


Private Sub ApplyDiagramTextFormat( _
                ByVal swTable As Object, _
                ByVal r As Long, _
                ByVal c As Long)


    ApplyCellFormat _
        swTable, _
        r, _
        c, _
        "Consolas", _
        DIAGRAM_FONT_POINTS, _
        False, _
        DIAGRAM_CHAR_SPACING

End Sub


' ============================================================
' TEXT HELPERS
' ============================================================

Private Function BuildAlignedAngleLine( _
                ByVal leftLabel As String, _
                ByVal rightLabel As String, _
                ByVal leftCenter As Long, _
                ByVal rightCenter As Long, _
                ByVal totalWidth As Long) As String


    Dim s As String


    s = Space$(totalWidth)


    Dim p1 As Long
    Dim p2 As Long


    p1 = _
        leftCenter - _
        Len(leftLabel) \ 2


    p2 = _
        rightCenter - _
        Len(rightLabel) \ 2


    If p1 < 1 Then p1 = 1


    If p2 + Len(rightLabel) - 1 > totalWidth Then


        p2 = _
            totalWidth - _
            Len(rightLabel) + 1


    End If


    Mid$(s, p1, Len(leftLabel)) = leftLabel

    Mid$(s, p2, Len(rightLabel)) = rightLabel


    BuildAlignedAngleLine = _
        RTrim$(s)

End Function


Private Function RepeatChar( _
                ByVal ch As String, _
                ByVal count As Long) As String


    Dim i As Long


    For i = 1 To count


        RepeatChar = _
            RepeatChar & ch


    Next i

End Function


' ============================================================
' NUMBERS
' ============================================================

Private Function AngleNumber( _
                ByVal value As String) As Double


    AngleNumber = _
        Val( _
            Replace( _
                CleanText(value), _
                ",", _
                "."))

End Function


Private Function NormalizeAngle( _
                ByVal value As String) As String


    NormalizeAngle = _
        Format$( _
            AngleNumber(value), _
            "0.########")

End Function


Private Function FormatAngle( _
                ByVal value As Double) As String


    If Abs( _
            value - Round(value, 0)) < _
       0.0001 Then


        FormatAngle = _
            CStr(CLng(Round(value, 0)))


    Else


        FormatAngle = _
            Replace( _
                Format$(value, "0.##"), _
                ",", _
                ".")


    End If

End Function


Private Function AngleEquals( _
                ByVal a As Double, _
                ByVal b As Double) As Boolean


    AngleEquals = _
        (Abs(a - b) < 0.000001)

End Function


Private Function MinAngle( _
                ByVal a As String, _
                ByVal b As String) As Double


    If AngleNumber(a) <= AngleNumber(b) Then

        MinAngle = AngleNumber(a)

    Else

        MinAngle = AngleNumber(b)

    End If

End Function


Private Function NumericTextValue( _
                ByVal value As String) As Double


    NumericTextValue = _
        Val( _
            Replace( _
                CleanText(value), _
                ",", _
                "."))

End Function


Private Function LengthNumber( _
                ByVal value As String) As Double


    LengthNumber = _
        NumericTextValue(value)

End Function


Private Function FormatDimensionMM( _
                ByVal valueMM As Double) As String


    If Abs( _
            valueMM - _
            Round(valueMM, 0)) < _
       0.15 Then


        FormatDimensionMM = _
            CStr( _
                CLng( _
                    Round(valueMM, 0)))


    Else


        FormatDimensionMM = _
            Replace( _
                Format$(valueMM, "0.0"), _
                ",", _
                ".")


    End If

End Function


Private Function SnapToNominalDimension( _
                ByVal valueMM As Double, _
                ByVal sourceHeight As String, _
                ByVal sourceWidth As String) As Double


    Dim h As Double
    Dim w As Double


    h = NumericTextValue(sourceHeight)
    w = NumericTextValue(sourceWidth)


    If h > 0# Then


        If Abs(valueMM - h) <= _
           NOMINAL_SNAP_TOL_MM Then


            SnapToNominalDimension = h

            Exit Function


        End If


    End If


    If w > 0# Then


        If Abs(valueMM - w) <= _
           NOMINAL_SNAP_TOL_MM Then


            SnapToNominalDimension = w

            Exit Function


        End If


    End If


    SnapToNominalDimension = _
        valueMM

End Function


' ============================================================
' VECTORS
' ============================================================

Private Function NormalizeVector( _
                ByVal v As Variant) As Variant


    Dim m As Double


    m = _
        Sqr( _
            v(0) * v(0) + _
            v(1) * v(1) + _
            v(2) * v(2))


    NormalizeVector = _
        Array( _
            v(0) / m, _
            v(1) / m, _
            v(2) / m)

End Function


Private Function DotProduct( _
                ByVal a As Variant, _
                ByVal b As Variant) As Double


    DotProduct = _
        a(0) * b(0) + _
        a(1) * b(1) + _
        a(2) * b(2)

End Function


' ============================================================
' CUT LIST / TABLE
' ============================================================

Private Function CountRowsInGroup( _
                ByVal count As Long, _
                ByRef rowGroup() As Long, _
                ByVal targetGroup As Long) As Long


    Dim i As Long


    For i = 1 To count


        If rowGroup(i) = targetGroup Then


            CountRowsInGroup = _
                CountRowsInGroup + 1


        End If


    Next i

End Function


Private Function ExtractCutListNumber( _
                ByVal value As String) As String


    Dim b As String


    b = _
        ExtractLastBracketValue(value)


    If b <> "" Then

        ExtractCutListNumber = b

    Else

        ExtractCutListNumber = _
            Trim$(value)

    End If

End Function


Private Function AppendUniqueNumber( _
                ByVal existingText As String, _
                ByVal newText As String) As String


    If Trim$(newText) = "" Then


        AppendUniqueNumber = _
            existingText


    ElseIf Trim$(existingText) = "" Then


        AppendUniqueNumber = _
            newText


    Else


        AppendUniqueNumber = _
            existingText & _
            "," & _
            newText


    End If

End Function


Private Function ExtractLastBracketValue( _
                ByVal value As String) As String


    Dim p1 As Long
    Dim p2 As Long


    p1 = InStrRev(value, "<")
    p2 = InStrRev(value, ">")


    If p1 > 0 And p2 > p1 Then


        ExtractLastBracketValue = _
            Mid$( _
                value, _
                p1 + 1, _
                p2 - p1 - 1)


    End If

End Function


Private Function GetLeadingDigits( _
                ByVal value As String) As String


    Dim i As Long
    Dim ch As String


    For i = 1 To Len(value)


        ch = _
            Mid$(value, i, 1)


        If ch >= "0" And ch <= "9" Then


            GetLeadingDigits = _
                GetLeadingDigits & ch


        Else


            Exit For


        End If


    Next i

End Function


Private Function IsSameDirection( _
                ByVal dir1 As String, _
                ByVal dir2 As String) As Boolean


    Dim s As String


    s = _
        UCase$( _
            dir1 & " " & dir2)


    Dim ruSame As String


    ruSame = _
        ChrW(1058) & _
        ChrW(1040) & _
        ChrW(1050)


    IsSameDirection = _
        (InStr( _
            1, _
            s, _
            ruSame, _
            vbTextCompare) > 0)

End Function


' ============================================================
' TABLE TEXT
' ============================================================

Private Function GetTableText( _
                ByVal swTable As Object, _
                ByVal r As Long, _
                ByVal c As Long) As String


    On Error Resume Next


    GetTableText = _
        swTable.DisplayedText2( _
            r, _
            c, _
            True)


    If Err.Number <> 0 Then


        Err.Clear


        GetTableText = _
            swTable.Text( _
                r, _
                c)


    End If


    On Error GoTo 0

End Function


Private Function CleanText( _
                ByVal value As String) As String


    Dim s As String


    s = value


    s = Replace(s, vbCr, " ")
    s = Replace(s, vbLf, " ")
    s = Replace(s, vbTab, " ")


    s = Trim$(s)


    Do While InStr(s, "  ") > 0


        s = _
            Replace(s, "  ", " ")


    Loop


    CleanText = s

End Function


Private Function SameText( _
                ByVal a As String, _
                ByVal b As String) As Boolean


    SameText = _
        (StrComp( _
            Trim$(a), _
            Trim$(b), _
            vbTextCompare) = 0)

End Function

