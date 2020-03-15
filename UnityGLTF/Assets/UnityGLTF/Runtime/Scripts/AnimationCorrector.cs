using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGLTF;
using GLTF.Schema;


public class AnimationCorrector
{
	public static string DealtWithBoneName = "root";

	public static bool Enable = false;
	public static bool DisableYMovement = false;
	public static bool DisableXZMovement = true;

	public static string FootName = "foot";
	private static string LeftFootName = FootName + "_L";
	private static string RightFootName = FootName + "_R";

	private string _dealtWithCurveBindingsPath;
	private string _leftFootCurveBindingsPath;
	private List<EditorCurveBinding> _leftFootCurveBindings;
	private string _rightFootCurveBindingsPath;
	private List<EditorCurveBinding> _rightFootCurveBindings;

	private List<GLTFSceneExporter.CurveBindingGroup> _dealtWithBoneParentCurveBindings = new List<GLTFSceneExporter.CurveBindingGroup>();
	private List<GLTFSceneExporter.CurveBindingGroup> _leftFootParentCurveBindings = new List<GLTFSceneExporter.CurveBindingGroup>();
	private List<GLTFSceneExporter.CurveBindingGroup> _rightFootParentCurveBindings = new List<GLTFSceneExporter.CurveBindingGroup>();

	private Vector3 _initialPoint = Vector3.zero;


	public void Init(List<GLTFSceneExporter.CurveBindingGroup> curveBindingGroups, Transform rootNodeTransform)
	{
		if (!Enable) return;
		Transform leftFootBone = null;
		Transform rightFootBone = null;

		Dictionary<string, GLTFSceneExporter.CurveBindingGroup> curveBindingGroupsDict = new Dictionary<string, GLTFSceneExporter.CurveBindingGroup>();

		foreach (var curveBindingGroup in curveBindingGroups)
		{
			curveBindingGroupsDict.Add(curveBindingGroup.path, curveBindingGroup);

			var bone = rootNodeTransform.Find(curveBindingGroup.path);

			if (bone.name == LeftFootName)
			{
				foreach (var property in curveBindingGroup.properties)
				{
					if ("m_LocalPosition" == property.name)
					{
						_leftFootCurveBindings = property.curveBindings;
					}
				}

				_leftFootCurveBindingsPath = curveBindingGroup.path;
				leftFootBone = bone;
			}

			if (bone.name == RightFootName)
			{
				foreach (var property in curveBindingGroup.properties)
				{
					if ("m_LocalPosition" == property.name)
					{
						_rightFootCurveBindings = property.curveBindings;
					}
				}

				_rightFootCurveBindingsPath = curveBindingGroup.path;
				rightFootBone = bone;
			}

			if (bone.name == DealtWithBoneName)
			{
				_dealtWithCurveBindingsPath = curveBindingGroup.path;
			}
		}

		if (null == leftFootBone || null == rightFootBone)
		{
			Debug.LogError("Invalid skeleton structure.");
			return;
		}

		var leftFoot = rootNodeTransform.transform.worldToLocalMatrix.MultiplyPoint3x4(leftFootBone.position);
		var rightFoot = rootNodeTransform.transform.worldToLocalMatrix.MultiplyPoint3x4(rightFootBone.position);

		_initialPoint = (leftFoot + rightFoot) / 2.0f;

		GetAllParents(_dealtWithCurveBindingsPath, ref _dealtWithBoneParentCurveBindings, curveBindingGroupsDict);
		GetAllParents(_leftFootCurveBindingsPath, ref _leftFootParentCurveBindings, curveBindingGroupsDict);
		GetAllParents(_rightFootCurveBindingsPath, ref _rightFootParentCurveBindings, curveBindingGroupsDict);
	}

	public Vector3[] Corrector(AnimationClip animationClip,
		string propertyName,
		List<EditorCurveBinding> curveBindings,
		int frameCount,
		string curveBindingGroupPath,
		out GLTFAnimationChannelPath path)
	{
		if (!Enable)
		{
			path = GLTFAnimationChannelPath.translation;
			return null;
		}

		if (curveBindingGroupPath != _dealtWithCurveBindingsPath)
		{
			path = GLTFAnimationChannelPath.translation;
			return null;
		}

		switch (propertyName)
		{
			case "m_LocalPosition":
				{
					var data = new Vector3[frameCount];
					for (var i = 0; i < frameCount; ++i)
					{
						var time = i / animationClip.frameRate;

						Vector3 frameData = Vector3.zero;
						GetPostionInWorldSpace(time, animationClip, curveBindings, _dealtWithBoneParentCurveBindings, ref frameData);

						Vector3 left = Vector3.zero;
						Vector3 right = Vector3.zero;
						GetPostionInWorldSpace(time, animationClip, _leftFootCurveBindings, _leftFootParentCurveBindings, ref left);
						GetPostionInWorldSpace(time, animationClip, _rightFootCurveBindings, _rightFootParentCurveBindings, ref right);

						Vector3 newPoint = (left + right) / 2.0f;
						Vector3 offset = newPoint - _initialPoint;
						offset = new Vector3(DisableXZMovement ? offset.x : 0.0f,
							DisableYMovement ? offset.y : 0.0f,
							DisableXZMovement ? offset.z : 0.0f);

						var corrFrameData = frameData - offset;
						GetPostionInParentSpace(time, animationClip, curveBindings, _dealtWithBoneParentCurveBindings, ref corrFrameData);

						data[i] = corrFrameData;
					}

					path = GLTFAnimationChannelPath.translation;
					return data;
				}
			case "m_LocalRotation":
				{
					path = GLTFAnimationChannelPath.rotation;
					return null;
				}
			case "m_LocalScale":
				{
					path = GLTFAnimationChannelPath.scale;
					return null;
				}
			case "localEulerAnglesRaw":
				{
					throw new Exception("Parsing of localEulerAnglesRaw is not supported.");
				}
		}

		Debug.LogError("Unrecognized property name: " + propertyName);
		path = GLTFAnimationChannelPath.translation;
		return null;
	}

	private void GetPostionInWorldSpace(float time,
		AnimationClip animationClip,
		List<EditorCurveBinding> curveBindings,
		List<GLTFSceneExporter.CurveBindingGroup> parentCurveBindings,
		ref Vector3 position)
	{
		var curveIndex = 0;
		var value = new float[3];
		foreach (var curveBinding in curveBindings)
		{
			var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
			if (curve != null) value[curveIndex++] = curve.Evaluate(time);
		}

		position = new Vector3(value[0], value[1], value[2]);

		foreach (var curveBindingGroup in parentCurveBindings)
		{
			Vector3 translation = Vector3.zero;
			Quaternion quaternion = Quaternion.identity;
			Vector3 scale = Vector3.one;

			foreach (var property in curveBindingGroup.properties)
			{
				switch (property.name)
				{
					case "m_LocalPosition":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							translation = new Vector3(v[0], v[1], v[2]);
							break;
						}
					case "m_LocalRotation":
						{
							var ci = 0;
							var v = new float[4];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							quaternion = new Quaternion(v[0], v[1], v[2], v[3]);
							break;
						}
					case "m_LocalScale":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							scale = new Vector3(v[0], v[1], v[2]);
							break;
						}
				}
			}

			Matrix4x4 m = Matrix4x4.TRS(translation, quaternion.normalized, scale);
			position = m.MultiplyPoint3x4(position);
		}
	}

	private void GetPostionInParentSpace(float time,
		AnimationClip animationClip,
		List<EditorCurveBinding> curveBindings,
		List<GLTFSceneExporter.CurveBindingGroup> parentCurveBindings,
		ref Vector3 postion)
	{
		for (int i = parentCurveBindings.Count - 1; 0 <= i; --i)
		{
			Vector3 translation = Vector3.zero;
			Quaternion quaternion = Quaternion.identity;
			Vector3 scale = Vector3.one;

			var curveBindingGroup = parentCurveBindings[i];

			foreach (var property in curveBindingGroup.properties)
			{
				switch (property.name)
				{
					case "m_LocalPosition":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							translation = new Vector3(v[0], v[1], v[2]);
							break;
						}
					case "m_LocalRotation":
						{
							var ci = 0;
							var v = new float[4];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							quaternion = new Quaternion(v[0], v[1], v[2], v[3]);
							break;
						}
					case "m_LocalScale":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							scale = new Vector3(v[0], v[1], v[2]);
							break;
						}
				}
			}

			Matrix4x4 m = Matrix4x4.TRS(translation, quaternion.normalized, scale);
			postion = m.inverse.MultiplyPoint3x4(postion);
		}
	}
	
	private void GetAllParents(string curveBindingsPath,
		ref List<GLTFSceneExporter.CurveBindingGroup> parentCurveBindings,
		Dictionary<string, GLTFSceneExporter.CurveBindingGroup> curveBindingGroups)
	{
		var path = curveBindingsPath;
		var removeStart = path.Length;
		while (removeStart != -1)
		{
			removeStart = path.LastIndexOf("/");
			if (removeStart != -1)
			{
				path = path.Remove(removeStart);

				GLTFSceneExporter.CurveBindingGroup parent;
				if (curveBindingGroups.TryGetValue(path, out parent))
				{
					parentCurveBindings.Add(parent);
				}
			}
		}
	}
}
