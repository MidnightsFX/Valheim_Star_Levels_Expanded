# Colorization (ColorSettings.yaml)

[All documentation](README.md)

In vanilla there are few creatures which can be colorized when they level up. Star Level Systems changes that. 
Most all creatures can be colorized, it should be noted that some creatures (Yagluth eg) do not colorize well and the effect is generally not very noticeable.

There are two different ways to apply colorization values to creatures.

- Creature specific color definitions. 
  These are split between default definitions (applied to any creature, if it does not have a more specific entry) 
  and character specific entries. These will be applied at the keyed level to the specified creature.
	```
	  Greydwarf:
		1:
		  hue: -0.06
		  saturation: 0.1
		  value: 0.05
	```
- Color Range definitions. These are ranges of color that will be sliced up and generate gradually changing color patterns 
  based on the ranges between the start and end points.
  ```
  DefaultGenerator:
  - characterSpecific: false
    startColorDef:
      hue: 0.07130837
      saturation: 0.05205864
      value: 0.01721987
    endColorDef:
      hue: -0.07488244
      saturation: 0.09342755
      value: -0.1008582
    rangeStart: 1
    rangeEnd: 15
  ```
  In this example, level ranges for default colors from level 1 to 15 will form a range of colors from hue 0.07130837 -> -0.07488244 etc.
  If you want to see the output from a generator you can enable the debug flag to dump the generated colorization config to a file. 
  You can use this to hand-pick color values etc.
