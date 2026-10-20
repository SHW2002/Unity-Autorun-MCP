# Script directory categories

Click the Script selector in the Auto Run window to open Unity's native selection
menu. The main window shows only the selected script and its controls; source
directories become cascading submenus. The selected script is checked, and
choosing a menu item updates the selection and closes the menu.

The menu groups registered scripts by their source directory below
`Assets/Editor/AutorunScripts`, with compatibility for `Assets/AutorunScripts/Editor`.
No extra category attribute is required. Scripts at the root and sources outside these roots remain
available. Package built-ins are grouped under Basic.

Directory navigation uses Unity's native submenu interaction, with no custom
file-tree popup. Type filtering applies independently of directory grouping, so project-defined
Basic scripts keep their actual source directory.

Selecting a script retains its ID, parameter editor, configuration menu, details
and execution controls. Categories refresh with the script catalog; the window
does not scan the filesystem during every repaint.
