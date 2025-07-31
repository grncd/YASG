import argparse
import subprocess
import sys
import os
import tempfile
import ctypes

# NOTE: We do not import 'requests' here.
# It will be imported dynamically after checking if it's installed.

# --- Script Configuration ---
FFMPEG_URL = "https://github.com/icedterminal/ffmpeg-installer/releases/download/7.0.0.20240429/FFmpeg_Full.msi"
FFMPEG_FILENAME = "FFmpeg_Full.msi"

# --- Helper Functions ---

def print_progress(percentage, message=""):
    """Prints a formatted progress indicator."""
    bar = f"[{percentage:3d}%]"
    # Center the message in the remaining space
    width = 80
    message_part = f" {message} "
    output = f"{bar}{message_part:-<{width - len(bar)}}"
    print(output)

def is_admin():
    """Checks if the script is running with administrative privileges."""
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False

def is_ffmpeg_installed():
    """Checks if ffmpeg is available in the system's PATH."""
    try:
        subprocess.run(
            ["ffmpeg", "-version"],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )
        return True
    except (FileNotFoundError, subprocess.CalledProcessError):
        return False

def run_command(command, description):
    """Runs a command in the shell and checks for errors."""
    print(f"-> Running: {description}...")
    try:
        subprocess.run(command, check=True, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        print(f"-> SUCCESS: {description} completed.")
    except subprocess.CalledProcessError as e:
        print(f"ERROR: Failed to {description.lower()}.")
        print(f"Stderr: {e.stderr.decode('utf-8')}")
        sys.exit(1)
    except FileNotFoundError:
        print(f"ERROR: Command not found. Make sure '{command[0]}' is in your system's PATH.")
        sys.exit(1)

def install_ffmpeg():
    """Checks for FFmpeg, then downloads and installs it if not found."""
    if is_ffmpeg_installed():
        print("-> SUCCESS: FFmpeg is already installed. Skipping.\n")
        return

    print("-> INFO: FFmpeg not found. Proceeding with installation.")
    try:
        import requests
    except ImportError:
        print("-> INFO: 'requests' library not found. Installing it first...")
        try:
            subprocess.run([sys.executable, "-m", "pip", "install", "requests"], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            print("-> SUCCESS: 'requests' has been installed.")
            import requests
        except subprocess.CalledProcessError as e:
            print("FATAL ERROR: Could not install 'requests' library, which is required to download FFmpeg.")
            print(f"Stderr: {e.stderr.decode('utf-8')}")
            sys.exit(1)

    temp_dir = tempfile.gettempdir()
    installer_path = os.path.join(temp_dir, FFMPEG_FILENAME)

    try:
        print(f"-> Downloading FFmpeg from {FFMPEG_URL}...")
        with requests.get(FFMPEG_URL, stream=True) as r:
            r.raise_for_status()
            with open(installer_path, 'wb') as f:
                for chunk in r.iter_content(chunk_size=8192): f.write(chunk)
        print("-> SUCCESS: Download complete.")
        
        print("-> Installing FFmpeg (per-user, no admin required)...")
        # Use /qn ALLUSERS=2 MSIINSTALLPERUSER=1 for per-user install
        msi_command = f'msiexec /i "{installer_path}" /qn ALLUSERS=2 MSIINSTALLPERUSER=1'
        subprocess.run(msi_command, check=True, shell=True)
        print("-> SUCCESS: FFmpeg installation finished.\n")
        print("NOTE: A terminal restart may be needed for FFmpeg to be in the PATH.")
    except (requests.exceptions.RequestException, subprocess.CalledProcessError) as e:
        print(f"ERROR: Failed during FFmpeg setup. Details: {e}")
        sys.exit(1)
    finally:
        if os.path.exists(installer_path):
            os.remove(installer_path)

def main():
    """Main function to parse arguments and run installation steps."""

    parser = argparse.ArgumentParser(
        description="A script to set up a Python environment with FFmpeg, spotdl, syrics, and optionally demucs."
    )
    parser.add_argument(
        "install_demucs", choices=['true', 'false'],
        help="Specify 'true' to install demucs, or 'false' to skip it."
    )
    args = parser.parse_args()

    # --- SCRIPT EXECUTION START ---
    print_progress(0, "Starting Environment Setup")

    print_progress(10, "Checking/Downloading for FFmpeg")
    install_ffmpeg()

    print_progress(30, "Installing spotdl")
    run_command(f'"{sys.executable}" -m pip install spotdl', "pip install spotdl")

    print_progress(60, "Installing syrics")
    run_command(f'"{sys.executable}" -m pip install syrics', "pip install syrics")

    if args.install_demucs == 'true':
        print_progress(85, "Installing demucs (this can take a while)")
        run_command(f'"{sys.executable}" -m pip install demucs', "pip install demucs")
    else:
        print("\n-> Skipping demucs installation as per argument.")

    print_progress(100, "Setup Complete!")
    print("\n===================================")
    print("All tasks completed successfully!")
    print("===================================")

if __name__ == "__main__":
    main()