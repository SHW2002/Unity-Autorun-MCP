# Script directory categories

Click the Script selector in the Auto Run window to open Unity's native selection
menu. The main window shows the selected script and its controls. The menu has
two levels, a business category and its scripts. The selected script is checked, and
choosing a menu item updates the selection and closes the menu.

The first directory below `Assets/Editor/AutorunScripts` supplies the category,
with compatibility for `Assets/AutorunScripts/Editor`. For example, a script in
`Assets/Editor/AutorunScripts/牵手/游泳` appears directly under `牵手`. Make the
first directory a short business name such as `启动` or `衣柜`. The menu uses
`其他` for business scripts without a category directory and `基础` for basic
scripts without a category directory, including package built-ins.

Type filtering applies independently of directory grouping, so project-defined
basic scripts can use the same business categories.

Selecting a script retains its ID, parameter editor, configuration menu, details
and execution controls. Categories refresh with the script catalog; the window
does not scan the filesystem during every repaint.
