using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;

public partial class Terrain : Node
{
	[Export] private Texture2D _terrainTexture;
	[Export] private MeshInstance3D _terrainMesh;
	[Export] private float _sdfDistMod = 10.0f;
	[Export] private Vector2I _defaultSize = new Vector2I(1024, 1024);

	public static int SizeOverride = -1;

	public Rid DistanceField => _texture2Drd.TextureRdRid;
	public Vector2I Size => _textureSize;
	public Action DistanceFieldCreated;
	public float SDFDistMod => _sdfDistMod;
	public List<Vector2I> EnemySpawn => _enemySpawnList;
	public List<Vector2I> PlayerSpawn => _playerSpawnList;
	
	private Vector2I _workgroupSize = new Vector2I(32, 32);
	private Vector2I _textureSize;
	private Texture2D _theTexture;

	private Rid _voronoiSeedShader;
	private Rid _jumpFloodShader;
	private Rid _distanceFieldShader;
	
	private Rid _voronoiSeedPipeline;
	private Rid _jumpFlooPipeline;
	private Rid _distanceFieldPipeline;
	
	private bool _generatedVoronoiSeed;
	private bool _generatedVoronoi;
	private bool _generatedDistanceField;

	private Rid[] _swapTextures = new Rid[2];
	private Dictionary<Rid, Rid[]> _swapSets = new Dictionary<Rid, Rid[]>();
	private int _currentSwap;

	private RenderingDevice _rd;
	private RDTextureFormat _format;
	private Texture2Drd _texture2Drd;

	private Rid _enemySpawnBuffer;
	private List<Vector2I> _enemySpawnList = new List<Vector2I>();
	private Rid _playerSpawnBuffer;
	private List<Vector2I> _playerSpawnList = new List<Vector2I>();

	private Random _rng = new Random();

	public override void _Ready()
	{
		NoiseTexture2D noiseTex = new NoiseTexture2D();
		if (_terrainTexture == null)
		{
			noiseTex.Width = _defaultSize.X;
			noiseTex.Height = _defaultSize.Y;
			if (SizeOverride != -1)
			{
				noiseTex.Width = SizeOverride;
				noiseTex.Height = SizeOverride;
			}
			noiseTex.GenerateMipmaps = false;
			noiseTex.Seamless = true;
			noiseTex.Normalize = true;
			FastNoiseLite noise = new FastNoiseLite();
			noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular;
			noise.Seed = _rng.Next();
			noise.Frequency = 0.01f;
			noiseTex.Noise = noise;
			_theTexture = noiseTex;
		}
		else
		{
			_theTexture = _terrainTexture;
		}
			
		_texture2Drd = new Texture2Drd();
		_textureSize = new Vector2I((int) _theTexture.GetSize().X, (int) _theTexture.GetSize().Y);
		
		_format = new RDTextureFormat();
		_format.Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat;
		_format.Width = (uint) _textureSize.X;
		_format.Height = (uint) _textureSize.Y;
		_format.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
		                    RenderingDevice.TextureUsageBits.ColorAttachmentBit |
		                    RenderingDevice.TextureUsageBits.StorageBit |
		                    RenderingDevice.TextureUsageBits.CanUpdateBit |
		                    RenderingDevice.TextureUsageBits.CanCopyToBit;
		
		_rd = RenderingServer.GetRenderingDevice();

		{
			RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://assets/terrain/compute/voronoi_seed.glsl");
			RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
			_voronoiSeedShader = _rd.ShaderCreateFromSpirV(shaderBytecode);
			_voronoiSeedPipeline = _rd.ComputePipelineCreate(_voronoiSeedShader);
		}
		
		{
			RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://assets/terrain/compute/jump_flood.glsl");
			RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
			_jumpFloodShader = _rd.ShaderCreateFromSpirV(shaderBytecode);
			_jumpFlooPipeline = _rd.ComputePipelineCreate(_jumpFloodShader);
		}
		
		{
			RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://assets/terrain/compute/distance_field.glsl");
			RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
			_distanceFieldShader = _rd.ShaderCreateFromSpirV(shaderBytecode);
			_distanceFieldPipeline = _rd.ComputePipelineCreate(_distanceFieldShader);
		}

		CreateSwapTextures();
		
		ShaderMaterial material = _terrainMesh.GetActiveMaterial(0) as ShaderMaterial;
		material?.SetShaderParameter("_sdf", _texture2Drd);
		_terrainMesh.Scale = new Vector3(_textureSize.X, 1.0f, _textureSize.Y);
		
		DebugImGui.Instance.RegisterWindow("terrain_params", "Terrain Parameters", _ImGuiTerrain);

		if (Game.SimulationMode)
		{
			DebugImGui.Instance.SetCustomWindowEnabled("terrain_params", true);
		}
	}
	
	private void _ImGuiTerrain()
	{
		ImGui.Text("---------------------------------------------------------------");
		
		ImGui.TextWrapped("Terrain size (will reload the map). Higher resolutions might be unstable.");
		if (ImGui.Button("256")) {
			SizeOverride = 256;
			GetTree().ReloadCurrentScene();
		}
		ImGui.SameLine();
		if (ImGui.Button("512")) {
			SizeOverride = 512;
			GetTree().ReloadCurrentScene();
		}
		ImGui.SameLine();
		if (ImGui.Button("1024")) {
			SizeOverride = 1024;
			GetTree().ReloadCurrentScene();
		}
		ImGui.SameLine();
		if (ImGui.Button("2048")) {
			SizeOverride = 2048;
			GetTree().ReloadCurrentScene();
		}
		ImGui.SameLine();
		if (ImGui.Button("4096")) {
			SizeOverride = 4096;
			GetTree().ReloadCurrentScene();
		}
		ImGui.SameLine();
		if (ImGui.Button("8192")) {
			SizeOverride = 8192;
			GetTree().ReloadCurrentScene();
		}
	}

	private void CreateSwapTextures()
	{
		_swapTextures[0] = _rd.TextureCreate(_format, new RDTextureView());
		_swapTextures[1] = _rd.TextureCreate(_format, new RDTextureView());
		_rd.TextureClear(_swapTextures[0], Colors.Teal, 0, 1, 0, 1);
		_rd.TextureClear(_swapTextures[1], Colors.Teal, 0, 1, 0, 1);
		_swapSets[_voronoiSeedShader] = new Rid[2];
		_swapSets[_voronoiSeedShader][0] = CreateUniform(_swapTextures[0], _voronoiSeedShader);
		_swapSets[_voronoiSeedShader][1] = CreateUniform(_swapTextures[1], _voronoiSeedShader);
		_swapSets[_jumpFloodShader] = new Rid[2];
		_swapSets[_jumpFloodShader][0] = CreateUniform(_swapTextures[0], _jumpFloodShader);
		_swapSets[_jumpFloodShader][1] = CreateUniform(_swapTextures[1], _jumpFloodShader);
		_swapSets[_distanceFieldShader] = new Rid[2];
		_swapSets[_distanceFieldShader][0] = CreateUniform(_swapTextures[0], _distanceFieldShader);
		_swapSets[_distanceFieldShader][1] = CreateUniform(_swapTextures[1], _distanceFieldShader);
	}

	private Rid CreateUniform(Rid rid, Rid shader)
	{
		RDUniform uniform = new RDUniform();
		uniform.UniformType = RenderingDevice.UniformType.Image;
		uniform.Binding = 0;
		uniform.AddId(rid);
		Rid uniformRid = _rd.UniformSetCreate([uniform], shader, 0);
		Utils.Assert(uniformRid.IsValid, "Failed to create uniform.");
		return uniformRid;
	}

	private void GenerateVoronoiSeed()
	{
		Image noiseTexImage = _theTexture.GetImage();
		Image noiseImage = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		for (int x = 0; x < _textureSize.X; x++) {
			for (int y = 0; y < _textureSize.Y; y++) {
				Color col = noiseTexImage.GetPixel(x, y);
				noiseImage.SetPixel(x, y, col);
			}
		}

		RDTextureFormat noiseFormat = new RDTextureFormat();
		noiseFormat.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
		noiseFormat.Width = (uint) _textureSize.X;
		noiseFormat.Height = (uint) _textureSize.Y;
		noiseFormat.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
		                        RenderingDevice.TextureUsageBits.ColorAttachmentBit |
		                        RenderingDevice.TextureUsageBits.StorageBit |
		                        RenderingDevice.TextureUsageBits.CanUpdateBit |
		                        RenderingDevice.TextureUsageBits.CanCopyToBit;
		
		Rid noiseTextureRid = _rd.TextureCreate(noiseFormat, new RDTextureView(), [noiseImage.GetData()]);
		Rid noiseUniformRid = CreateUniform(noiseTextureRid, _voronoiSeedShader);
		
		ExecuteCompute(_voronoiSeedPipeline, [_swapSets[_jumpFloodShader][InputSwapIndex], 
			_swapSets[_jumpFloodShader][OutputSwapIndex], noiseUniformRid]);
		
		_texture2Drd.TextureRdRid = _swapTextures[OutputSwapIndex];
		
		_generatedVoronoiSeed = true;
	}

	private int _jumpFloodPass = 0;
	private void GenerateVoronoi()
	{
		// Number of passes required is the log2 of the largest viewport dimension rounded up to the nearest power of 2.
		int passes = Mathf.CeilToInt(Mathf.Log(Mathf.Max(_textureSize.X, _textureSize.Y)) / Mathf.Log(2.0f));
		for (; _jumpFloodPass < passes;)
		{
			// Offset for each pass is half the previous one, starting at half the square resolution rounded up to nearest power 2.
			//i.e. for 768x512 we round up to 1024x1024 and the offset for the first pass is 512x512, then 256x256, etc. 
			float offset = Mathf.Pow(2, passes - _jumpFloodPass - 1);
			
			float[] constants = [offset, 0.0f, 0.0f, 0.0f];
			byte[] constantsByte = new byte[constants.Length * 4];
			Buffer.BlockCopy(constants, 0, constantsByte, 0, constantsByte.Length);

			// Switch our swap textures, so the previous output becomes the input. If this is the first pass,
			// the input will now be the output of the Voronoi seed pass.
			_currentSwap = (_currentSwap + 1) % 2;
			
			ExecuteCompute(_jumpFlooPipeline, [_swapSets[_jumpFloodShader][InputSwapIndex], 
				_swapSets[_jumpFloodShader][OutputSwapIndex]], (computeList) =>
			{
				_rd.ComputeListSetPushConstant(computeList, constantsByte, (uint) constantsByte.Length);
			});

			_jumpFloodPass++;
			
			_texture2Drd.TextureRdRid = _swapTextures[OutputSwapIndex];
			break;
		}

		if (_jumpFloodPass == passes)
		{
			_generatedVoronoi = true;
		}
	}

	private void GenerateDistanceField()
	{
		_currentSwap = (_currentSwap + 1) % 2;
		
		float[] constants = [_sdfDistMod, Game.SimulationMode ? 1.0f : 0.0f, 0.0f, 0.0f];
		byte[] constantsByte = new byte[constants.Length * 4];
		Buffer.BlockCopy(constants, 0, constantsByte, 0, constantsByte.Length);

		uint safePositionsSize = (uint) (_textureSize.X * _textureSize.Y);
		_enemySpawnBuffer = _rd.StorageBufferCreate(safePositionsSize * 4);
		_rd.BufferClear(_enemySpawnBuffer, 0, safePositionsSize * 4);
		_playerSpawnBuffer = _rd.StorageBufferCreate(safePositionsSize * 4);
		_rd.BufferClear(_playerSpawnBuffer, 0, safePositionsSize * 4);
		
		// Enemy spawn set.
		Rid enemySpawnUniform;
		{
			RDUniform uniform = new RDUniform();
			uniform.UniformType = RenderingDevice.UniformType.StorageBuffer;
			uniform.Binding = 0;
			uniform.AddId(_enemySpawnBuffer);
			enemySpawnUniform = _rd.UniformSetCreate([uniform], _distanceFieldShader, 2);
			Utils.Assert(enemySpawnUniform.IsValid, "Failed to create uniform.");
		}

		// Player spawn set.
		Rid playerSpawnUniform;
		{
			RDUniform uniform = new RDUniform();
			uniform.UniformType = RenderingDevice.UniformType.StorageBuffer;
			uniform.Binding = 0;
			uniform.AddId(_playerSpawnBuffer);
			playerSpawnUniform = _rd.UniformSetCreate([uniform], _distanceFieldShader, 3);
			Utils.Assert(playerSpawnUniform.IsValid, "Failed to create uniform.");
		}

		ExecuteCompute(_distanceFieldPipeline, [_swapSets[_jumpFloodShader][InputSwapIndex], 
			_swapSets[_jumpFloodShader][OutputSwapIndex]], (computeList) =>
		{
			_rd.ComputeListBindUniformSet(computeList, enemySpawnUniform, 2);
			_rd.ComputeListBindUniformSet(computeList, playerSpawnUniform, 3);
			_rd.ComputeListSetPushConstant(computeList, constantsByte, (uint) constantsByte.Length);
		});
		
		// Even though we only need to store a bit per position (pixel) in the distance field, GLSL doesn't support
		// 8-bit types so we'll use an int for each. TODO: pack bit data instead.
		byte[] enemySpawns = _rd.BufferGetData(_enemySpawnBuffer, 0, safePositionsSize * 4);
		Span<int> enemySpawnsSpan = MemoryMarshal.Cast<byte, int>(new Span<byte>(enemySpawns, 0, enemySpawns.Length));
		for (int i = 0; i < enemySpawnsSpan.Length; i++)
		{
			if (enemySpawnsSpan[i] == 0) continue;
			_enemySpawnList.Add(new Vector2I(i / _textureSize.X, i % _textureSize.Y) - _textureSize / 2);
		}
		byte[] playerSpawns = _rd.BufferGetData(_playerSpawnBuffer, 0, safePositionsSize * 4);
		Span<int> playerSpawnsSpan = MemoryMarshal.Cast<byte, int>(new Span<byte>(playerSpawns, 0, playerSpawns.Length));
		for (int i = 0; i < playerSpawnsSpan.Length; i++)
		{
			if (playerSpawnsSpan[i] == 0) continue;
			_playerSpawnList.Add(new Vector2I(i / _textureSize.X, i % _textureSize.Y) - _textureSize / 2);
		}
		
		_texture2Drd.TextureRdRid = _swapTextures[OutputSwapIndex];
		
		_generatedDistanceField = true;
	}

	private int InputSwapIndex => _currentSwap;
	private int OutputSwapIndex => (_currentSwap + 1) % 2;

	private void ExecuteCompute(Rid pipeline, IReadOnlyList<Rid> uniforms, Action<long> extra = null)
	{
		long computeList = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(computeList, pipeline);

		for (uint i = 0; i < uniforms.Count; i++)
		{
			Rid uniform = uniforms[(int) i];
			_rd.ComputeListBindUniformSet(computeList, uniform, i);
		}
		
		extra?.Invoke(computeList);
		
		Vector2I workgroupCount = new Vector2I(_textureSize.X / _workgroupSize.X, _textureSize.Y / _workgroupSize.Y);
		_rd.ComputeListDispatch(computeList, (uint) workgroupCount.X, (uint) workgroupCount.Y, 1);
		_rd.ComputeListEnd();
		_rd.Submit();
		_rd.Sync();
	}

	public override void _Process(double delta)
	{
		if (!_generatedVoronoiSeed)
		{
			GenerateVoronoiSeed();
		}

		while (!_generatedVoronoi)
		{
			GenerateVoronoi();
		}
		
		if (!_generatedDistanceField)
		{
			GenerateDistanceField();
			DistanceFieldCreated?.Invoke();
		}
	}
}
