# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
from pyrevit import revit, forms

doc = revit.doc

def make_unique_name(base_name, existing_names):
    """Trả về tên unique bằng cách thêm số tăng dần nếu trùng"""
    if base_name not in existing_names:
        return base_name
    i = 1
    while True:
        new_name = "{}_{}".format(base_name, i)
        if new_name not in existing_names:
            return new_name
        i += 1

# --- B1: Chọn sheet ---
sheets = forms.select_sheets(title="Chọn các Sheet để copy")
if not sheets:
    forms.alert("Không có sheet nào được chọn!", exitscript=True)

copied_sheets = []
copied_views = []

# Lấy tất cả tên sheet và view hiện có để kiểm tra trùng
all_sheet_names = {s.Name for s in FilteredElementCollector(doc).OfClass(ViewSheet)}
all_view_names = {v.Name for v in FilteredElementCollector(doc).OfClass(View)}

t = Transaction(doc, "Copy Sheets with Views")
t.Start()

try:
    for sheet in sheets:
        # Lấy TitleBlock type
        titleblocks = FilteredElementCollector(doc, sheet.Id)\
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)\
                        .WhereElementIsNotElementType()\
                        .ToElements()
        if not titleblocks:
            raise Exception("Sheet {} không có Titleblock".format(sheet.SheetNumber))

        tb_instance = titleblocks[0]
        tb_type = doc.GetElement(tb_instance.GetTypeId())

        # Đặt tên sheet mới (unique)
        base_name = sheet.Name + "_MTO"
        new_name = make_unique_name(base_name, all_sheet_names)
        all_sheet_names.add(new_name)

        base_number = sheet.SheetNumber + "_MTO"
        new_number = make_unique_name(base_number, {s.SheetNumber for s in FilteredElementCollector(doc).OfClass(ViewSheet)})

        new_sheet = ViewSheet.Create(doc, tb_type.Id)
        new_sheet.Name = new_name
        new_sheet.SheetNumber = new_number
        copied_sheets.append(new_sheet)

        # Copy views
        for vp_id in sheet.GetAllViewports():
            vp = doc.GetElement(vp_id)
            view = doc.GetElement(vp.ViewId)

            # Duplicate with detailing
            new_view_id = view.Duplicate(ViewDuplicateOption.WithDetailing)
            new_view = doc.GetElement(new_view_id)

            # Đổi tên view (Copy -> MTO) + unique
            new_view_name = new_view.Name.replace("Copy", "MTO")
            new_view_name = make_unique_name(new_view_name, all_view_names)
            new_view.Name = new_view_name
            all_view_names.add(new_view_name)

            # Thêm viewport vào sheet mới
            Viewport.Create(doc, new_sheet.Id, new_view.Id, vp.GetBoxCenter())
            copied_views.append(new_view)

    t.Commit()

except Exception as e:
    t.RollBack()
    forms.alert("Có lỗi xảy ra:\n{}".format(str(e)), exitscript=True)

# --- B4: Thông báo ---
msg = "Đã copy {} sheet và {} view.".format(len(copied_sheets), len(copied_views))
forms.alert(msg, exitscript=False)
