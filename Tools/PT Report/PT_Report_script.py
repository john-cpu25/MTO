from Autodesk.Revit.DB import*
from Autodesk.Revit.Exceptions import*
from pyrevit import forms, script
from __units__ import*
from rpw.ui.forms import*
from __PT_display__ import*


uidoc = __revit__.ActiveUIDocument
doc = __revit__.ActiveUIDocument.Document
active_view = doc.ActiveView

__title__ = "PT Report"
__authors__ = "Jason Le"
__doc__ = "Create a table to review all PTs in active view"


output = script.get_output()
PT_table = []
tendon_list = get_all_PT()
tendon_list = sort_tendons_by_PT_ID(tendon_list)
warning_char = " **"

components = [  Label('Select Parameters to report:'),
                CheckBox('isChecked_NumberStrands','Number of Strands', default = True),
                CheckBox('isChecked_NumberSplitStrands','Number of Splitted Strands', default = False),
                CheckBox('isChecked_StrandType','Strand Type', default = True),
                CheckBox('isChecked_Length','PT Length', default = True),
                CheckBox('isChecked_SplitStrandsLength','Length of Splitted Strands', default = False),
                CheckBox('isChecked_Weight','PT Weight', default = True),
                CheckBox('isChecked_PanCount','Pan Count', default = True),
                CheckBox('isChecked_AssociatedLevel','PT Associated Level', default = False),
                CheckBox('isChecked_AssociatedBuilding','PT Associated Building', default = False),
                CheckBox('isChecked_AssociatedZone','PT Associated Zone', default = False),
                CheckBox('isChecked_PourNumber','PT Pour Number', default = False),
                Separator(),
				CheckBox('isChecked_SelectAll', 'Select All', default = False),
				Separator(),
                Button('Select')
            ]

form = FlexForm ('PT Report', components)
form.show()
try:
	if form.values['isChecked_SelectAll'] == True:
		isChecked_NumberStrands 		= True
		isChecked_NumberSplitStrands	= True
		isChecked_StrandType 			= True
		isChecked_Length		 		= True
		isChecked_SplitStrandsLength	= True
		isChecked_Weight 				= True
		isChecked_PanCount 				= True
		isChecked_AssociatedLevel		= True
		isChecked_AssociatedBuilding	= True
		isChecked_AssociatedZone		= True
		isChecked_PourNumber			= True
	else:
		isChecked_NumberStrands 		= form.values['isChecked_NumberStrands']
		isChecked_NumberSplitStrands	= form.values['isChecked_NumberSplitStrands']
		isChecked_StrandType 			= form.values['isChecked_StrandType']
		isChecked_Length		 		= form.values['isChecked_Length']
		isChecked_SplitStrandsLength	= form.values['isChecked_SplitStrandsLength']
		isChecked_Weight 				= form.values['isChecked_Weight']
		isChecked_PanCount 				= form.values['isChecked_PanCount']
		isChecked_AssociatedLevel		= form.values['isChecked_AssociatedLevel']
		isChecked_AssociatedBuilding	= form.values['isChecked_AssociatedBuilding']
		isChecked_AssociatedZone		= form.values['isChecked_AssociatedZone']
		isChecked_PourNumber			= form.values['isChecked_PourNumber']
except KeyError:
	sys.exit()
except Exception as exception_string:
	forms.alert(str(exception_string), exitscript=True)


total_strands = 0
total_length = 0
total_weight = 0
total_pan = 0

for tendon in tendon_list:
	elid_link = output.linkify(tendon.Id)
	PT_ID = tendon.LookupParameter("PT ID No.").AsInteger()
	number_strands = tendon.LookupParameter("Number of Strands").AsInteger()
	display_split_strands = tendon.LookupParameter("Split Strands").AsInteger()
	if display_split_strands == 0:
		number_split_strands = 0
		split_strand_length = "0 mm"
	else:
		number_split_strands = tendon.LookupParameter("No. Strands Terminated Early").AsInteger()
		split_strand_length = str(int(ft2mm(tendon.LookupParameter("Split Strands Length").AsDouble()))) + " mm"

	strand_type = tendon.LookupParameter("Strand Type").AsString()
	length = int(ft2mm(tendon.LookupParameter("PT Length").AsDouble()))
	weight = round(tendon.LookupParameter("PT Weight").AsDouble(), 1)
	level = tendon.LookupParameter("PT Associated Level").AsString()
	building = '-'#tendon.LookupParameter("PT Associated Building").AsString()
	zone = '-'#tendon.LookupParameter("PT Associated Zone").AsString()
	pour_number = tendon.LookupParameter("PT Pour Number").AsString()
	pan_count = tendon.LookupParameter("Pan Count").AsInteger()

	total_strands = total_strands + number_strands
	total_length = total_length + length
	total_weight = total_weight + weight
	total_pan = total_pan + pan_count

	if tendon_list.index(tendon) > 0:
		if PT_ID != PT_previous_ID + 1:
			PT_ID_table = str(PT_ID) + warning_char
		else:
			PT_ID_table = str(PT_ID)
	else:
		PT_ID_table = str(PT_ID)

	if number_strands < 3 or number_strands > 5:
		number_strands = str(number_strands) + warning_char

	if length < 5000:
		length = str(length) + " mm" + warning_char
	else:
		length = str(length) + " mm"

	if level == "":
		level = "---"
	if building == "":
		building = "---"
	if zone == "":
		zone = "---"

	PT_previous_ID = PT_ID

	PT_property = [elid_link, PT_ID_table]

	if isChecked_NumberStrands == True:
		PT_property.append(number_strands)
	if isChecked_NumberSplitStrands == True:
		PT_property.append(number_split_strands)
	if isChecked_StrandType == True:
		PT_property.append(strand_type)
	if isChecked_Length == True:
		PT_property.append(length)
	if isChecked_SplitStrandsLength == True:
		PT_property.append(split_strand_length)
	if isChecked_Weight == True:
		PT_property.append(weight)
	if isChecked_PanCount == True:
		PT_property.append(pan_count)
	if isChecked_AssociatedLevel == True:
		PT_property.append(level)
	if isChecked_AssociatedBuilding == True:
		PT_property.append(building)
	if isChecked_AssociatedZone == True:
		PT_property.append(zone)
	if isChecked_PourNumber == True:
		PT_property.append(pour_number)

	PT_table.append(PT_property)


column_titles = ["ID Tag", "PT ID"]
format_list = ['','']
total_list = ["Total", len(tendon_list)]

if isChecked_NumberStrands == True:
	column_titles.append("No. Strands")
	format_list.append('')
	total_list.append(total_strands)
if isChecked_NumberSplitStrands == True:
	column_titles.append("No. Strands Terminated Early")
	format_list.append('')
	total_list.append('')
if isChecked_StrandType == True:
	column_titles.append("Strand Type")
	format_list.append('')
	total_list.append('')
if isChecked_Length == True:
	column_titles.append("PT Length")
	format_list.append('')
	total_list.append(str(total_length) + " mm")
if isChecked_SplitStrandsLength == True:
	column_titles.append("Length of Strands Terminated Early")
	format_list.append('')
	total_list.append('')
if isChecked_Weight == True:
	column_titles.append("PT Weight")
	format_list.append('{} kg')
	total_list.append(total_weight)
if isChecked_PanCount == True:
	column_titles.append("Pan Count")
	format_list.append('')
	total_list.append(total_pan)
if isChecked_AssociatedLevel == True:
	column_titles.append("PT Associated Level")
	format_list.append('')
	total_list.append('')
if isChecked_AssociatedBuilding == True:
	column_titles.append("PT Associated Building")
	format_list.append('')
	total_list.append('')
if isChecked_AssociatedZone == True:
	column_titles.append("PT Associated Zone")
	format_list.append('')
	total_list.append('')
if isChecked_PourNumber == True:
	column_titles.append("Pour Number")
	format_list.append('')
	total_list.append('')

PT_table.append(total_list)
output.print_table(table_data = PT_table,
					title = "PT data table",
					columns = column_titles,
					formats = format_list,
					last_line_style='color:red;')

	# print("{}\t|\t{}".format(elid_link, PT_ID))
