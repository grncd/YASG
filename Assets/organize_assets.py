

import os
import shutil

def organize_assets():
    # Get the absolute path of the directory where the script is located
    base_dir = os.path.dirname(os.path.abspath(__file__))
    print(f"Running organization script in: {base_dir}")

    # Define the target directories
    dirs_to_create = {
        "Animations", "Materials", "Plugins", 
        "Scripts", "Settings", "Sounds"
    }

    # Create directories if they don't exist
    for dir_name in dirs_to_create:
        path = os.path.join(base_dir, dir_name)
        if not os.path.exists(path):
            print(f"Creating directory: {path}")
            os.makedirs(path)

    # --- Mappings ---
    # 1. Files by extension
    file_mappings = {
        ".cs": "Scripts",
        ".asset": "Settings",
        ".mixer": "Sounds",
        ".mat": "Materials",
        ".shader": "Materials",
        ".anim": "Animations"
    }

    # 2. Specific folders to be moved into "Plugins"
    plugin_folders = [
        "ERP", "FishNet", "MPUIKit", "PlayerPrefsEditor"
    ]

    # --- Execution ---
    items = os.listdir(base_dir)

    for item_name in items:
        source_path = os.path.join(base_dir, item_name)
        
        # Skip this script itself and its meta file
        if item_name == "organize_assets.py" or item_name == "organize_assets.py.meta":
            continue

        # --- Handle Files ---
        if os.path.isfile(source_path):
            # Check for file extension mapping
            _, ext = os.path.splitext(item_name)
            if ext in file_mappings:
                target_folder_name = file_mappings[ext]
                target_dir = os.path.join(base_dir, target_folder_name)
                
                # Move the file
                print(f"Moving file '{item_name}' to '{target_folder_name}'...")
                shutil.move(source_path, os.path.join(target_dir, item_name))
                
                # Move the corresponding .meta file
                meta_file = item_name + ".meta"
                meta_source_path = os.path.join(base_dir, meta_file)
                if os.path.exists(meta_source_path):
                    print(f"Moving meta file '{meta_file}' to '{target_folder_name}'...")
                    shutil.move(meta_source_path, os.path.join(target_dir, meta_file))

        # --- Handle Plugin Folders ---
        elif os.path.isdir(source_path):
            if item_name in plugin_folders:
                target_dir = os.path.join(base_dir, "Plugins")
                print(f"Moving plugin folder '{item_name}' to 'Plugins'...")
                shutil.move(source_path, os.path.join(target_dir, item_name))

    print("\nOrganization complete!")
    print("You may now delete the 'organize_assets.py' and 'organize_assets.py.meta' files.")

if __name__ == "__main__":
    organize_assets()

