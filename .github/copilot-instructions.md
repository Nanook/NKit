# Copilot Instructions

## Project Guidelines
- When verifying DataStore reconstructions with multiple parts against a single-part image record, the parts should be joined for comparison using CRC/Size summation if a part count mismatch is detected. This ensures individual files like .app files match the overall logical image hash stored in the DataStore.