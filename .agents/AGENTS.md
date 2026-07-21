# MTO Smart Tag Rules
- **Only tag already tagged items**: The logic for `OnlyAlreadyTagged = true` in `MtoSmartTagHandler.cs` is confirmed to be correct. Do NOT modify the behavior of this case in future updates unless explicitly requested by the user.
- **Only tag untagged items**: The logic for `OnlyUntagged = true` in `MtoSmartTagHandler.cs` is confirmed to be correct. 
  - Tag placement MUST be perfectly straight relative to the rebar line (achieved via dynamic outward offset vector).
  - For LBar with COG (checked via parameters COG1-8), the Tag is placed at the shared corner (intersection) of the longest segment, NOT the free end of the short hook.
  Do NOT modify this behavior in future updates unless explicitly requested by the user.
