﻿using UnityEngine;

namespace StandardAssets.Characters.CharacterInput
{
	/// <summary>
	/// A configuration of the platform and controller IDs
	/// </summary>
	/// <remarks>
	/// This is consumed by <see cref="LegacyCharacterInputDevicesCache"/>
	/// </remarks>
	[CreateAssetMenu(fileName = "LegacyCharacterInputDevices", menuName = "Standard Assets/Characters/Input/Create Input Device Mapping",
		order = 1)]
	public class LegacyCharacterInputDevices : ScriptableObject
	{
		[SerializeField, Tooltip("Platform IDs")]
		protected string macPlatformId = "OSX", windowsPlatformId = "Windows";

		[SerializeField, Tooltip("Controller IDs")]
		protected string xboxOneControllerId = "XBone", xbox360ControllerId = "XBox360", ps4ControllerId = "PS4", xboxOneWirelessControllerId = "XBoneWireless";

		/// <summary>
		/// Axis naming convention
		/// </summary>
		/// <remarks>
		/// Specific syntax is used in the convention
		/// </remarks>
		/// <example>{control}{controller}{platform} will resolve as SprintXBoneOSX</example>
		[SerializeField, Tooltip("Expected keywords: {control} {controller} {platform}. Case sensitive")]
		protected string controlConvention = "{control}{controller}{platform}";

		public string macPlatformIdentifier
		{
			get { return macPlatformId; }
		}

		public string windowsPlatformIdentifier
		{
			get { return windowsPlatformId; }
		}

		public string xboxOneControllerIdentifier
		{
			get { return xboxOneControllerId; }
		}

		public string xboxOneWirelessControllerIdentifier
		{
			get { return xboxOneWirelessControllerId; }
		}

		public string xbox360ControllerIdentifier
		{
			get { return xbox360ControllerId; }
		}

		public string ps4ControllerIdentifier
		{
			get { return ps4ControllerId; }
		}

		public string controlConventions
		{
			get { return controlConvention; }
		}
	}
}