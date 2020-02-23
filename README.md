# fMotionBlur
Motion Blur for Universal RP (as same as PPSv2)

This feature should be useful for you who don't wait MotionVector for official support.  
MotionVectorPass is faster than Legacy RP by supporting SRPBatcher.  
MotionBlurPass is same as that of PPSv2.

![image](https://user-images.githubusercontent.com/24952685/75114171-39eb6500-5697-11ea-83a7-1991033c0f74.png)

## How to use
- Import PostProcessingStackV2 package. This sample project use the MotionBlur.shader of PPSv2.
- Add ScriptableRendererFeature(fMotion Feature) to FowardRendererData.
- Add VolumeComponent(fMotion Blur) to PostProcessVolume.
![image](https://user-images.githubusercontent.com/24952685/75114194-656e4f80-5697-11ea-903e-a559c86da5a5.png)

## Known issue
- If ScriptableRendererFeature is missing when this project was reimported, please close the project and reopen.
- Run MotionVectorPass to renderers that is stopping. These are skipped in Legacy RP.

## License
Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License). 

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
